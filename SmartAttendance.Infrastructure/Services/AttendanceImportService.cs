using System.Data;
using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceImports.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

/// <summary>
/// استيراد سجلات الحضور من ملفات أجهزة البصمة (Excel/CSV): التطبيع، كشف
/// التكرار، والإدراج الدفعي — يخدم صفحة /AttendanceImports.
/// </summary>
public class AttendanceImportService : IAttendanceImportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _dbContext;

    /// <summary>سقف أخطاء القراءة المحفوظة للعرض — تجميعها كلها يعيد الانفجار.</summary>
    private const int MaxReportedParseErrors = AttendanceImportLimits.MaxReportedErrors;

    /// <summary>حجم دفعة SqlBulkCopy.</summary>
    private const int BulkBatchSize = 10000;

    private static readonly TimeOnly NightShiftMoveFrom = new(0, 0);
    private static readonly TimeOnly NightShiftMoveTo = new(6, 30);

    public AttendanceImportService(IUnitOfWork unitOfWork, ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public async Task<AttendanceImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        AttendanceImportScope scope,
        int previewLimit = 500,
        CancellationToken cancellationToken = default)
    {
        var build = await BuildAsync(filePath, scope, previewLimit, cancellationToken);

        return new AttendanceImportPreviewViewModel
        {
            Token = token,
            FileName = originalFileName,
            TotalRawRows = build.TotalRawRows,
            TotalGroups = build.TotalGroups,
            ReadyToImportCount = build.ReadyCount + build.WarningCount,
            WarningCount = build.WarningCount,
            ErrorCount = build.ErrorCount,
            ExistingRecordsCount = build.ExistingCount,
            PreviewLimit = previewLimit,
            Rows = build.PreviewRows
        };
    }

    public async Task<AttendanceImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName,
        AttendanceImportScope scope,
        CancellationToken cancellationToken = default)
    {
        // لا نبني صفوف العرض عند الاستيراد — يكفي التجميع نفسه.
        var build = await BuildAsync(filePath, scope, previewLimit: 0, cancellationToken);

        var importedCount = await BulkInsertAsync(build, originalFileName, cancellationToken);
        var skippedCount = build.TotalGroups - importedCount;

        return new AttendanceImportResultViewModel
        {
            ImportedCount = importedCount,
            SkippedCount = skippedCount,
            WarningImportedCount = build.WarningCount,
            ErrorCount = build.ErrorCount,
            Message = $"Imported {importedCount} attendance records. Skipped {skippedCount} rows/groups."
        };
    }

    /// <summary>
    /// يقرأ الملف **متدفّقاً** ويجمّعه إلى سجلّات يومية، ويحسب العدادات بمرور
    /// واحد. لا يُبنى كائن عرضٍ إلا لأعلى <paramref name="previewLimit"/> صفّاً —
    /// وهذا جوهر الإصلاح: النسخة السابقة كانت تبني مليونَي كائن ثم تقتطع 500.
    /// </summary>
    private async Task<AttendanceBuild> BuildAsync(
        string filePath,
        AttendanceImportScope scope,
        int previewLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.AllowsSelectedCompany())
        {
            throw new UnauthorizedAccessException("The selected company is outside the attendance import scope.");
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length <= 0 ||
            fileInfo.Length > AttendanceImportLimits.MaxCompressedBytes)
        {
            throw new InvalidOperationException(
                "Attendance import file is missing or exceeds the configured size limit.");
        }

        var build = new AttendanceBuild();
        var aggregator = new AttendancePunchAggregator();
        var elapsed = Stopwatch.StartNew();

        foreach (var line in StreamPunchLines(filePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > TimeSpan.FromSeconds(AttendanceImportLimits.MaxProcessingSeconds))
            {
                throw new TimeoutException("Attendance import exceeded the processing time limit.");
            }
            build.TotalRawRows++;
            if (build.TotalRawRows > AttendanceImportLimits.MaxWorksheetRows)
            {
                throw new InvalidOperationException(
                    "Attendance import exceeds the maximum worksheet row count.");
            }

            if (!line.Ok)
            {
                build.ErrorCount++;

                // نحتفظ بعيّنة من أخطاء القراءة فقط — تجميعها كلها يعيد الانفجار.
                if (build.ParseErrors.Count < MaxReportedParseErrors)
                {
                    build.ParseErrors.Add(new RawPunchError
                    {
                        EmployeeNo = line.EmployeeNo,
                        Message = line.Error
                    });
                }

                continue;
            }

            aggregator.Add(
                line.EmployeeNo,
                line.AttendanceDate,
                line.PunchTime,
                line.FunctionType,
                line.MachineName);
            if (aggregator.DayCount > AttendanceImportLimits.MaxEmployeeDayGroups)
            {
                throw new InvalidOperationException(
                    "Attendance import exceeds the maximum employee-day group count.");
            }
        }

        build.TotalGroups = aggregator.DayCount;

        var employeeLookup = await LoadEmployeeLookupAsync(
            scope.SelectedCompanyId,
            cancellationToken);
        var existingKeys = await LoadExistingKeysAsync(
            aggregator,
            scope.SelectedCompanyId,
            cancellationToken);

        var top = previewLimit > 0
            ? new SortedSet<PreviewCandidate>(PreviewCandidateComparer.Instance)
            : null;

        foreach (var pair in aggregator.Days)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var employeeNo = aggregator.EmployeeNumberAt(
                AttendancePunchAggregator.EmployeeIndexOf(pair.Key));
            var date = AttendancePunchAggregator.DateOf(pair.Key);

            employeeLookup.TryGetValue(NormalizeKey(employeeNo), out var employee);

            var status = ResolveStatus(employee, date, pair.Value, existingKeys);

            switch (status)
            {
                case RowStatus.Error: build.ErrorCount++; break;
                case RowStatus.Existing: build.ExistingCount++; break;
                case RowStatus.Warning: build.WarningCount++; break;
                default: build.ReadyCount++; break;
            }

            if (status is RowStatus.Ready or RowStatus.Warning && employee is not null)
            {
                build.Importable.Add(new ImportableDay(
                    employee.Id,
                    date,
                    new DateTime(pair.Value.FirstPunchTicks),
                    pair.Value.PunchCount > 1 ? new DateTime(pair.Value.LastPunchTicks) : null,
                    pair.Value.PunchCount,
                    aggregator.DescribeMachines(pair.Value.MachineMask)));
            }

            if (top is null)
            {
                continue;
            }

            top.Add(new PreviewCandidate(status, date, employeeNo, pair.Key));

            // مجموعة مقيّدة: نحتفظ بأفضل previewLimit فقط بدل ترتيب المليونين.
            if (top.Count > previewLimit)
            {
                top.Remove(top.Max);
            }
        }

        if (top is not null)
        {
            build.PreviewRows = BuildPreviewRows(top, aggregator, employeeLookup);
        }

        return build;
    }

    private List<AttendanceImportRowViewModel> BuildPreviewRows(
        SortedSet<PreviewCandidate> candidates,
        AttendancePunchAggregator aggregator,
        Dictionary<string, Employee> employeeLookup)
    {
        var rows = new List<AttendanceImportRowViewModel>(candidates.Count);

        foreach (var candidate in candidates)
        {
            aggregator.TryGetDay(candidate.Key, out var day);
            employeeLookup.TryGetValue(NormalizeKey(candidate.EmployeeNo), out var employee);

            rows.Add(new AttendanceImportRowViewModel
            {
                EmployeeId = employee?.Id,
                EmployeeNo = candidate.EmployeeNo,
                EmployeeName = employee?.FullName ?? string.Empty,
                AttendanceDate = candidate.Date,
                CheckIn = new DateTime(day.FirstPunchTicks),
                CheckOut = day.PunchCount > 1 ? new DateTime(day.LastPunchTicks) : null,
                PunchCount = day.PunchCount,
                FunctionTypes = aggregator.DescribeFunctions(day.FunctionMask),
                MachineNames = aggregator.DescribeMachines(day.MachineMask),
                Status = StatusName(candidate.Status),
                Message = StatusMessage(candidate.Status, day.PunchCount),
                CanImport = candidate.Status is RowStatus.Ready or RowStatus.Warning
            });
        }

        return rows;
    }

    private static RowStatus ResolveStatus(
        Employee? employee,
        DateOnly date,
        AttendancePunchAggregator.DayAggregate day,
        HashSet<long> existingKeys)
    {
        if (employee is null)
        {
            return RowStatus.Error;
        }

        if (existingKeys.Contains(ExistingKey(employee.Id, date)))
        {
            return RowStatus.Existing;
        }

        return day.PunchCount == 1 || day.PunchCount > 2
            ? RowStatus.Warning
            : RowStatus.Ready;
    }

    private static string StatusName(RowStatus status) => status switch
    {
        RowStatus.Error => "Error",
        RowStatus.Warning => "Warning",
        RowStatus.Existing => "Existing",
        _ => "Ready"
    };

    private static string StatusMessage(RowStatus status, int punchCount) => status switch
    {
        RowStatus.Error => "Employee number not found in Employees table.",
        RowStatus.Existing => "Attendance record already exists for this employee and date. Skipped to avoid duplicates.",
        RowStatus.Warning when punchCount == 1 => "Single punch only. It will be imported with missing Check Out.",
        RowStatus.Warning => $"More than two punches found ({punchCount}). First punch will be Check In and last punch will be Check Out.",
        _ => "Ready to import."
    };

    private static long ExistingKey(int employeeId, DateOnly date) =>
        ((long)employeeId << 32) | (uint)date.DayNumber;

    private async Task<Dictionary<string, Employee>> LoadEmployeeLookupAsync(
        int selectedCompanyId,
        CancellationToken cancellationToken)
    {
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.EmployeeNo != null && x.EmployeeNo != string.Empty)
            .Where(x => x.CompanyId == selectedCompanyId ||
                (x.CompanyId == null && x.Branch.CompanyId == selectedCompanyId))
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<string, Employee>(StringComparer.Ordinal);

        foreach (var employee in employees)
        {
            lookup.TryAdd(NormalizeKey(employee.EmployeeNo), employee);
        }

        return lookup;
    }

    /// <summary>
    /// مفاتيح السجلات القائمة **بنطاق تواريخ الملف فقط**. النسخة السابقة كانت
    /// تسحب جدول الحضور كلّه للذاكرة لتفحص التكرار.
    /// </summary>
    private async Task<HashSet<long>> LoadExistingKeysAsync(
        AttendancePunchAggregator aggregator,
        int selectedCompanyId,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<long>();

        if (aggregator.DayCount == 0)
        {
            return keys;
        }

        var minDay = int.MaxValue;
        var maxDay = int.MinValue;

        foreach (var pair in aggregator.Days)
        {
            var dayNumber = AttendancePunchAggregator.DateOf(pair.Key).DayNumber;
            if (dayNumber < minDay) minDay = dayNumber;
            if (dayNumber > maxDay) maxDay = dayNumber;
        }

        var from = DateOnly.FromDayNumber(minDay);
        var to = DateOnly.FromDayNumber(maxDay);

        var existing = await _dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.AttendanceDate >= from && x.AttendanceDate <= to)
            .Where(x => x.Employee.CompanyId == selectedCompanyId ||
                (x.Employee.CompanyId == null && x.Employee.Branch.CompanyId == selectedCompanyId))
            .Select(x => new { x.EmployeeId, x.AttendanceDate })
            .ToListAsync(cancellationToken);

        foreach (var record in existing)
        {
            keys.Add(ExistingKey(record.EmployeeId, record.AttendanceDate));
        }

        return keys;
    }

    private static string BuildImportNotes(string originalFileName, AttendanceImportRowViewModel row)
    {
        var notes =
            $"Imported from {Path.GetFileName(originalFileName)} | Punches: {row.PunchCount} | Machines: {row.MachineNames}";

        if (notes.Length > 490)
            notes = notes[..490];

        return notes;
    }

    private static RawReadResult ReadRawPunches(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".xlsx" => ReadXlsxRawPunches(filePath),
            ".csv" => ReadCsvRawPunches(filePath),
            _ => throw new InvalidOperationException("Unsupported file type. Please upload .xlsx or .csv file.")
        };
    }

    private static RawReadResult ReadXlsxRawPunches(string filePath)
    {
        var result = new RawReadResult();

        using var archive = ZipFile.OpenRead(filePath);
        ValidateArchiveLimits(archive);

        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = GetFirstWorksheetPath(archive);

        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException("Worksheet not found inside Excel file.");

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = worksheetEntry.Open();
        var document = XDocument.Load(stream);

        var rows = document
            .Descendants(ns + "sheetData")
            .Elements(ns + "row")
            .ToList();

        if (rows.Count > AttendanceImportLimits.MaxWorksheetRows + 1)
        {
            throw new InvalidOperationException("Excel worksheet exceeds the maximum row count.");
        }

        if (!rows.Any())
            return result;

        var headerCells = ReadRowCells(rows.First(), sharedStrings, ns);
        var headers = headerCells
            .OrderBy(x => x.Key)
            .Select(x => x.Value)
            .ToList();

        var employeeIndex = FindHeaderIndex(headers, "EmployeeCardNumber", "EmployeeNo", "Employee No", "AC-No", "ACNo", "CardNo", "PIN");
        var dateIndex = FindHeaderIndex(headers, "AttendanceDate", "Attendance Date", "DateTime", "PunchTime", "Time", "Date");
        var functionIndex = FindHeaderIndex(headers, "FunctionType", "Function Type", "InOut", "In Out", "Status");
        var machineIndex = FindHeaderIndex(headers, "MachineName", "Machine Name", "Device", "DeviceName", "Device Name");

        if (employeeIndex < 0 || dateIndex < 0)
            throw new InvalidOperationException("Required columns not found. Expected EmployeeCardNumber and AttendanceDate.");

        foreach (var row in rows.Skip(1))
        {
            result.TotalRawRows++;

            var rowNumber = int.TryParse(row.Attribute("r")?.Value, out var rn) ? rn : result.TotalRawRows + 1;

            var cells = ReadRowCells(row, sharedStrings, ns);

            var employeeNo = GetCellValue(cells, employeeIndex).Trim();
            var dateText = GetCellValue(cells, dateIndex).Trim();
            var functionType = functionIndex >= 0 ? GetCellValue(cells, functionIndex).Trim() : string.Empty;
            var machineName = machineIndex >= 0 ? GetCellValue(cells, machineIndex).Trim() : string.Empty;

            AddRawPunch(result, rowNumber, employeeNo, dateText, functionType, machineName);
        }

        return result;
    }

    private static RawReadResult ReadCsvRawPunches(string filePath)
    {
        var result = new RawReadResult();

        var lines = File.ReadAllLines(filePath, Encoding.UTF8);

        if (lines.Length == 0)
            return result;

        var headers = SplitCsvLine(lines[0]);

        var employeeIndex = FindHeaderIndex(headers, "EmployeeCardNumber", "EmployeeNo", "Employee No", "AC-No", "ACNo", "CardNo", "PIN");
        var dateIndex = FindHeaderIndex(headers, "AttendanceDate", "Attendance Date", "DateTime", "PunchTime", "Time", "Date");
        var functionIndex = FindHeaderIndex(headers, "FunctionType", "Function Type", "InOut", "In Out", "Status");
        var machineIndex = FindHeaderIndex(headers, "MachineName", "Machine Name", "Device", "DeviceName", "Device Name");

        if (employeeIndex < 0 || dateIndex < 0)
            throw new InvalidOperationException("Required columns not found. Expected EmployeeCardNumber and AttendanceDate.");

        for (var i = 1; i < lines.Length; i++)
        {
            result.TotalRawRows++;

            var values = SplitCsvLine(lines[i]);

            var employeeNo = GetCsvValue(values, employeeIndex).Trim();
            var dateText = GetCsvValue(values, dateIndex).Trim();
            var functionType = functionIndex >= 0 ? GetCsvValue(values, functionIndex).Trim() : string.Empty;
            var machineName = machineIndex >= 0 ? GetCsvValue(values, machineIndex).Trim() : string.Empty;

            AddRawPunch(result, i + 1, employeeNo, dateText, functionType, machineName);
        }

        return result;
    }

    private static void AddRawPunch(
        RawReadResult result,
        int rowNumber,
        string employeeNo,
        string dateText,
        string functionType,
        string machineName)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            result.Errors.Add(new RawPunchError
            {
                EmployeeNo = "-",
                Message = $"Row {rowNumber}: Employee number is empty."
            });
            return;
        }

        if (!TryParseDateTime(dateText, out var punchTime))
        {
            result.Errors.Add(new RawPunchError
            {
                EmployeeNo = employeeNo,
                Message = $"Row {rowNumber}: Invalid AttendanceDate value ({dateText})."
            });
            return;
        }

        var attendanceDate = DateOnly.FromDateTime(punchTime);
        var punchTimeOnly = TimeOnly.FromDateTime(punchTime);

        if (punchTimeOnly >= NightShiftMoveFrom && punchTimeOnly <= NightShiftMoveTo)
            attendanceDate = attendanceDate.AddDays(-1);

        result.Punches.Add(new RawPunch
        {
            RowNumber = rowNumber,
            EmployeeNo = employeeNo,
            PunchTime = punchTime,
            AttendanceDate = attendanceDate,
            FunctionType = NormalizeFunctionType(functionType),
            MachineName = machineName
        });
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStrings = new List<string>();

        var entry = archive.GetEntry("xl/sharedStrings.xml");

        if (entry == null)
            return sharedStrings;

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        foreach (var si in document.Descendants(ns + "si"))
        {
            var text = string.Concat(si.Descendants(ns + "t").Select(x => x.Value));
            sharedStrings.Add(text);
            if (sharedStrings.Count > AttendanceImportLimits.MaxSharedStrings)
            {
                throw new InvalidOperationException(
                    "Excel file exceeds the maximum shared-string count.");
            }
        }

        return sharedStrings;
    }

    private static void ValidateArchiveLimits(ZipArchive archive)
    {
        long decompressedTotal = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > AttendanceImportLimits.MaxZipEntryBytes)
            {
                throw new InvalidOperationException("Excel archive contains an oversized entry.");
            }

            decompressedTotal = checked(decompressedTotal + entry.Length);
            if (decompressedTotal > AttendanceImportLimits.MaxDecompressedBytes)
            {
                throw new InvalidOperationException(
                    "Excel archive exceeds the decompressed-size limit.");
            }

            if (entry.Length > 0 &&
                (entry.CompressedLength <= 0 ||
                 entry.Length / (double)entry.CompressedLength >
                 AttendanceImportLimits.MaxCompressionRatio))
            {
                throw new InvalidOperationException(
                    "Excel archive exceeds the allowed compression ratio.");
            }
        }
    }

    private static string GetFirstWorksheetPath(ZipArchive archive)
    {
        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("workbook.xml not found.");

        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("workbook relationships not found.");

        using var workbookStream = workbookEntry.Open();
        var workbookDocument = XDocument.Load(workbookStream);

        var firstSheet = workbookDocument
            .Descendants(mainNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No sheets found in workbook.");

        var relationshipId = firstSheet.Attribute(relNs + "id")?.Value;

        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidOperationException("First worksheet relationship id not found.");

        using var relsStream = relsEntry.Open();
        var relsDocument = XDocument.Load(relsStream);

        var relationship = relsDocument
            .Descendants(packageRelNs + "Relationship")
            .FirstOrDefault(x => x.Attribute("Id")?.Value == relationshipId)
            ?? throw new InvalidOperationException("Worksheet relationship not found.");

        var target = relationship.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("Worksheet target not found.");

        if (target.StartsWith("/"))
            return target.TrimStart('/');

        return "xl/" + target.TrimStart('/');
    }

    private static Dictionary<int, string> ReadRowCells(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        XNamespace ns)
    {
        var values = new Dictionary<int, string>();

        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var columnIndex = GetColumnIndex(reference);

            if (columnIndex < 0)
                continue;

            var type = cell.Attribute("t")?.Value;
            var rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;

            string value;

            if (type == "s" && int.TryParse(rawValue, out var sharedStringIndex) &&
                sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
            {
                value = sharedStrings[sharedStringIndex];
            }
            else if (type == "inlineStr")
            {
                value = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
            }
            else
            {
                value = rawValue;
            }

            values[columnIndex] = value;
        }

        return values;
    }

    private static int GetColumnIndex(string cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return -1;

        var letters = new string(cellReference
            .TakeWhile(char.IsLetter)
            .ToArray());

        if (string.IsNullOrWhiteSpace(letters))
            return -1;

        var index = 0;

        foreach (var letter in letters.ToUpperInvariant())
        {
            index *= 26;
            index += letter - 'A' + 1;
        }

        return index - 1;
    }

    private static string GetCellValue(Dictionary<int, string> cells, int index)
    {
        return cells.TryGetValue(index, out var value) ? value : string.Empty;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] names)
    {
        var normalizedNames = names
            .Select(NormalizeHeader)
            .ToHashSet();

        for (var i = 0; i < headers.Count; i++)
        {
            if (normalizedNames.Contains(NormalizeHeader(headers[i])))
                return i;
        }

        return -1;
    }

    private static string NormalizeHeader(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string NormalizeKey(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeFunctionType(string value)
    {
        var normalized = value.Trim();

        return normalized switch
        {
            "0" => "I",
            "1" => "O",
            "i" => "I",
            "o" => "O",
            _ => normalized
        };
    }

    private static bool TryParseDateTime(string text, out DateTime dateTime)
    {
        dateTime = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd HH:mm:ss",
            "MM/dd/yyyy HH:mm:ss",
            "M/d/yyyy H:mm:ss",
            "dd/MM/yyyy HH:mm:ss",
            "d/M/yyyy H:mm:ss",
            "yyyy-MM-ddTHH:mm:ss"
        };

        if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTime))
        {
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            return true;

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateTime))
            return true;

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericDate) &&
            numericDate > 20000 && numericDate < 90000)
        {
            dateTime = DateTime.FromOADate(numericDate);
            return true;
        }

        return false;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        result.Add(current.ToString());

        return result;
    }

    private static string GetCsvValue(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private class AttendanceImportBuildResult
    {
        public int TotalRawRows { get; set; }

        public List<AttendanceImportRowViewModel> Rows { get; set; } = new();
    }

    private class RawReadResult
    {
        public int TotalRawRows { get; set; }

        public List<RawPunch> Punches { get; set; } = new();

        public List<RawPunchError> Errors { get; set; } = new();
    }

    private class RawPunch
    {
        public int RowNumber { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public DateTime PunchTime { get; set; }

        public DateOnly AttendanceDate { get; set; }

        public string FunctionType { get; set; } = string.Empty;

        public string MachineName { get; set; } = string.Empty;
    }

    private class RawPunchError
    {
        public string EmployeeNo { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// إدراج دفعي بـ<c>SqlBulkCopy</c>. النسخة السابقة كانت تضيف كل سجلّ
    /// لمتتبِّع EF ثم تستدعي SaveChanges مرة واحدة — مليونا كيان بالذاكرة.
    /// </summary>
    private async Task<int> BulkInsertAsync(
        AttendanceBuild build,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        if (build.Importable.Count == 0)
        {
            return 0;
        }

        var fileLabel = Path.GetFileName(originalFileName);

        using var table = new DataTable();
        table.Columns.Add("EmployeeId", typeof(int));
        table.Columns.Add("AttendanceDate", typeof(DateTime));
        table.Columns.Add("CheckIn", typeof(DateTime));
        table.Columns.Add("CheckOut", typeof(DateTime));
        table.Columns.Add("Source", typeof(int));
        table.Columns.Add("Status", typeof(int));
        table.Columns.Add("Notes", typeof(string));

        foreach (var day in build.Importable)
        {
            var notes = $"Imported from {fileLabel} | Punches: {day.PunchCount} | Machines: {day.Machines}";
            if (notes.Length > 490) notes = notes[..490];

            table.Rows.Add(
                day.EmployeeId,
                day.Date.ToDateTime(TimeOnly.MinValue),
                day.CheckIn,
                (object?)day.CheckOut ?? DBNull.Value,
                (int)AttendanceSource.Device,
                (int)AttendanceStatus.Present,
                notes);
        }

        var connection = _dbContext.Database.GetDbConnection();

        if (connection is not SqlConnection sqlConnection)
        {
            throw new InvalidOperationException(
                "الإدراج الدفعي يتطلّب SQL Server.");
        }

        var opened = sqlConnection.State != ConnectionState.Open;
        if (opened) await sqlConnection.OpenAsync(cancellationToken);

        try
        {
            using var bulk = new SqlBulkCopy(sqlConnection)
            {
                DestinationTableName = "AttendanceRecords",
                BatchSize = BulkBatchSize,
                BulkCopyTimeout = AttendanceImportLimits.BulkCopyTimeoutSeconds,
                EnableStreaming = true
            };

            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulk.WriteToServerAsync(table, cancellationToken);
        }
        finally
        {
            if (opened) await sqlConnection.CloseAsync();
        }

        return build.Importable.Count;
    }

    /// <summary>
    /// يقرأ الملف سطراً سطراً بلا تحميله كاملاً. CSV يُقرأ متدفّقاً حقيقياً؛
    /// وxlsx يمرّ عبر القارئ القائم (ملفات الأجهزة الضخمة تأتي CSV عملياً).
    /// </summary>
    private static IEnumerable<PunchLine> StreamPunchLines(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".csv" => StreamCsvPunchLines(filePath),
            ".xlsx" => StreamXlsxPunchLines(filePath),
            _ => throw new InvalidOperationException(
                "Unsupported file type. Please upload .xlsx or .csv file.")
        };
    }

    private static IEnumerable<PunchLine> StreamCsvPunchLines(string filePath)
    {
        var rowNumber = 1;
        List<string>? headers = null;
        var employeeIndex = -1;
        var dateIndex = -1;
        var functionIndex = -1;
        var machineIndex = -1;

        // File.ReadLines كسولة: لا تُحمَّل الأسطر كلها للذاكرة.
        foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
        {
            if (headers is null)
            {
                headers = SplitCsvLine(line);

                employeeIndex = FindHeaderIndex(headers, "EmployeeCardNumber", "EmployeeNo", "Employee No", "AC-No", "ACNo", "CardNo", "PIN");
                dateIndex = FindHeaderIndex(headers, "AttendanceDate", "Attendance Date", "DateTime", "PunchTime", "Time", "Date");
                functionIndex = FindHeaderIndex(headers, "FunctionType", "Function Type", "InOut", "In Out", "Status");
                machineIndex = FindHeaderIndex(headers, "MachineName", "Machine Name", "Device", "DeviceName", "Device Name");

                if (employeeIndex < 0 || dateIndex < 0)
                {
                    throw new InvalidOperationException(
                        "Required columns not found. Expected EmployeeCardNumber and AttendanceDate.");
                }

                continue;
            }

            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = SplitCsvLine(line);

            yield return ParsePunchLine(
                rowNumber,
                GetCsvValue(values, employeeIndex).Trim(),
                GetCsvValue(values, dateIndex).Trim(),
                functionIndex >= 0 ? GetCsvValue(values, functionIndex).Trim() : string.Empty,
                machineIndex >= 0 ? GetCsvValue(values, machineIndex).Trim() : string.Empty);
        }
    }

    private static IEnumerable<PunchLine> StreamXlsxPunchLines(string filePath)
    {
        var raw = ReadXlsxRawPunches(filePath);

        foreach (var error in raw.Errors)
        {
            yield return PunchLine.Failed(error.EmployeeNo, error.Message);
        }

        foreach (var punch in raw.Punches)
        {
            yield return new PunchLine(
                punch.EmployeeNo,
                punch.AttendanceDate,
                punch.PunchTime,
                punch.FunctionType,
                punch.MachineName);
        }
    }

    /// <summary>
    /// يحوّل قيم صفٍّ خام إلى بصمة صالحة أو خطأ. **تعديل المناوبة الليلية هنا**
    /// (بصمة بعد منتصف الليل وقبل 6:30 تُنسب ليوم العمل السابق) — سلوك النسخة
    /// السابقة نفسه حرفياً حتى لا يتغيّر أي احتساب حضور.
    /// </summary>
    private static PunchLine ParsePunchLine(
        int rowNumber,
        string employeeNo,
        string dateText,
        string functionType,
        string machineName)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return PunchLine.Failed("-", $"Row {rowNumber}: Employee number is empty.");
        }

        if (!TryParseDateTime(dateText, out var punchTime))
        {
            return PunchLine.Failed(
                employeeNo,
                $"Row {rowNumber}: Invalid AttendanceDate value ({dateText}).");
        }

        var attendanceDate = DateOnly.FromDateTime(punchTime);
        var punchTimeOnly = TimeOnly.FromDateTime(punchTime);

        if (punchTimeOnly >= NightShiftMoveFrom && punchTimeOnly <= NightShiftMoveTo)
        {
            attendanceDate = attendanceDate.AddDays(-1);
        }

        return new PunchLine(employeeNo, attendanceDate, punchTime, functionType, machineName);
    }

    private enum RowStatus
    {
        Ready,
        Warning,
        Existing,
        Error
    }

    private readonly struct PunchLine
    {
        public PunchLine(
            string employeeNo,
            DateOnly attendanceDate,
            DateTime punchTime,
            string functionType,
            string machineName)
        {
            Ok = true;
            EmployeeNo = employeeNo;
            AttendanceDate = attendanceDate;
            PunchTime = punchTime;
            FunctionType = functionType;
            MachineName = machineName;
            Error = string.Empty;
        }

        private PunchLine(string employeeNo, string error)
        {
            Ok = false;
            EmployeeNo = employeeNo;
            AttendanceDate = default;
            PunchTime = default;
            FunctionType = string.Empty;
            MachineName = string.Empty;
            Error = error;
        }

        public bool Ok { get; }
        public string EmployeeNo { get; }
        public DateOnly AttendanceDate { get; }
        public DateTime PunchTime { get; }
        public string FunctionType { get; }
        public string MachineName { get; }
        public string Error { get; }

        public static PunchLine Failed(string employeeNo, string error) =>
            new(employeeNo, error);
    }

    private readonly record struct ImportableDay(
        int EmployeeId,
        DateOnly Date,
        DateTime CheckIn,
        DateTime? CheckOut,
        int PunchCount,
        string Machines);

    private readonly record struct PreviewCandidate(
        RowStatus Status,
        DateOnly Date,
        string EmployeeNo,
        long Key);

    /// <summary>
    /// ترتيب العرض كما كان: الأخطاء ثم التحذيرات، ثم الأحدث تاريخاً، ثم رقم
    /// الموظف. المفتاح آخرَ الترتيب ليضمن تمييز المتساويات داخل SortedSet.
    /// </summary>
    private sealed class PreviewCandidateComparer : IComparer<PreviewCandidate>
    {
        public static readonly PreviewCandidateComparer Instance = new();

        public int Compare(PreviewCandidate x, PreviewCandidate y)
        {
            var byStatus = Rank(y.Status).CompareTo(Rank(x.Status));
            if (byStatus != 0) return byStatus;

            var byDate = y.Date.CompareTo(x.Date);
            if (byDate != 0) return byDate;

            var byEmployee = string.CompareOrdinal(x.EmployeeNo, y.EmployeeNo);
            if (byEmployee != 0) return byEmployee;

            return x.Key.CompareTo(y.Key);
        }

        private static int Rank(RowStatus status) => status switch
        {
            RowStatus.Error => 3,
            RowStatus.Warning => 2,
            RowStatus.Existing => 1,
            _ => 0
        };
    }

    private sealed class AttendanceBuild
    {
        public int TotalRawRows { get; set; }
        public int TotalGroups { get; set; }
        public int ReadyCount { get; set; }
        public int WarningCount { get; set; }
        public int ExistingCount { get; set; }
        public int ErrorCount { get; set; }

        public List<RawPunchError> ParseErrors { get; } = new();
        public List<ImportableDay> Importable { get; } = new();
        public List<AttendanceImportRowViewModel> PreviewRows { get; set; } = new();
    }
}
