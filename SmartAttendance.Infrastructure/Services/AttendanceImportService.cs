using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceImports.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceImportService : IAttendanceImportService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly TimeOnly NightShiftMoveFrom = new(0, 0);
    private static readonly TimeOnly NightShiftMoveTo = new(6, 30);

    public AttendanceImportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AttendanceImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        int previewLimit = 500)
    {
        var buildResult = await BuildPreviewRowsAsync(filePath, originalFileName);

        return new AttendanceImportPreviewViewModel
        {
            Token = token,
            FileName = originalFileName,
            TotalRawRows = buildResult.TotalRawRows,
            TotalGroups = buildResult.Rows.Count,
            ReadyToImportCount = buildResult.Rows.Count(x => x.CanImport),
            WarningCount = buildResult.Rows.Count(x => x.Status == "Warning"),
            ErrorCount = buildResult.Rows.Count(x => x.Status == "Error"),
            ExistingRecordsCount = buildResult.Rows.Count(x => x.Status == "Existing"),
            PreviewLimit = previewLimit,
            Rows = buildResult.Rows
                .OrderByDescending(x => x.Status == "Error")
                .ThenByDescending(x => x.Status == "Warning")
                .ThenByDescending(x => x.AttendanceDate)
                .ThenBy(x => x.EmployeeNo)
                .Take(previewLimit)
                .ToList()
        };
    }

    public async Task<AttendanceImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName)
    {
        var buildResult = await BuildPreviewRowsAsync(filePath, originalFileName);

        var rowsToImport = buildResult.Rows
            .Where(x => x.CanImport && x.EmployeeId.HasValue && x.AttendanceDate.HasValue && x.CheckIn.HasValue)
            .ToList();

        var importedCount = 0;
        var warningImportedCount = 0;

        foreach (var row in rowsToImport)
        {
            var notes = BuildImportNotes(originalFileName, row);

            var attendanceRecord = new AttendanceRecord
            {
                EmployeeId = row.EmployeeId!.Value,
                AttendanceDate = row.AttendanceDate!.Value,
                CheckIn = row.CheckIn!.Value,
                CheckOut = row.CheckOut,
                Source = AttendanceSource.Device,
                Status = AttendanceStatus.Present,
                Notes = notes
            };

            await _unitOfWork.AttendanceRecords.AddAsync(attendanceRecord);

            importedCount++;

            if (row.Status == "Warning")
                warningImportedCount++;
        }

        if (importedCount > 0)
            await _unitOfWork.SaveChangesAsync();

        var skippedCount = buildResult.Rows.Count - importedCount;

        return new AttendanceImportResultViewModel
        {
            ImportedCount = importedCount,
            SkippedCount = skippedCount,
            WarningImportedCount = warningImportedCount,
            ErrorCount = buildResult.Rows.Count(x => x.Status == "Error"),
            Message = $"Imported {importedCount} attendance records. Skipped {skippedCount} rows/groups."
        };
    }

    private async Task<AttendanceImportBuildResult> BuildPreviewRowsAsync(
        string filePath,
        string originalFileName)
    {
        var rawReadResult = ReadRawPunches(filePath);

        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var existingRecords = (await _unitOfWork.AttendanceRecords.GetAllAsync()).ToList();

        var employeeLookup = employees
            .Where(x => !string.IsNullOrWhiteSpace(x.EmployeeNo))
            .GroupBy(x => NormalizeKey(x.EmployeeNo))
            .ToDictionary(x => x.Key, x => x.First());

        var existingLookup = existingRecords
            .GroupBy(x => $"{x.EmployeeId}|{x.AttendanceDate:yyyy-MM-dd}")
            .ToDictionary(x => x.Key, x => x.First());

        var rows = new List<AttendanceImportRowViewModel>();

        foreach (var error in rawReadResult.Errors)
        {
            rows.Add(new AttendanceImportRowViewModel
            {
                EmployeeNo = error.EmployeeNo,
                Status = "Error",
                Message = error.Message,
                CanImport = false
            });
        }

        var groupedPunches = rawReadResult.Punches
            .GroupBy(x => new
            {
                EmployeeNoKey = NormalizeKey(x.EmployeeNo),
                x.EmployeeNo,
                x.AttendanceDate
            })
            .ToList();

        foreach (var group in groupedPunches)
        {
            var punches = group
                .OrderBy(x => x.PunchTime)
                .ToList();

            var firstPunch = punches.First();
            var lastPunch = punches.Last();

            employeeLookup.TryGetValue(group.Key.EmployeeNoKey, out var employee);

            var row = new AttendanceImportRowViewModel
            {
                EmployeeId = employee?.Id,
                EmployeeNo = group.Key.EmployeeNo,
                EmployeeName = employee?.FullName ?? string.Empty,
                AttendanceDate = group.Key.AttendanceDate,
                CheckIn = firstPunch.PunchTime,
                CheckOut = punches.Count > 1 ? lastPunch.PunchTime : null,
                PunchCount = punches.Count,
                FunctionTypes = string.Join(", ", punches
                    .Select(x => x.FunctionType)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()),
                MachineNames = string.Join(", ", punches
                    .Select(x => x.MachineName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()),
                CanImport = true
            };

            if (employee == null)
            {
                row.Status = "Error";
                row.Message = "Employee number not found in Employees table.";
                row.CanImport = false;
            }
            else
            {
                var existingKey = $"{employee.Id}|{group.Key.AttendanceDate:yyyy-MM-dd}";

                if (existingLookup.ContainsKey(existingKey))
                {
                    row.Status = "Existing";
                    row.Message = "Attendance record already exists for this employee and date. Skipped to avoid duplicates.";
                    row.CanImport = false;
                }
                else if (punches.Count == 1)
                {
                    row.Status = "Warning";
                    row.Message = "Single punch only. It will be imported with missing Check Out.";
                    row.CanImport = true;
                }
                else if (punches.Count > 2)
                {
                    row.Status = "Warning";
                    row.Message = $"More than two punches found ({punches.Count}). First punch will be Check In and last punch will be Check Out.";
                    row.CanImport = true;
                }
                else
                {
                    row.Status = "Ready";
                    row.Message = "Ready to import.";
                    row.CanImport = true;
                }
            }

            rows.Add(row);
        }

        return new AttendanceImportBuildResult
        {
            TotalRawRows = rawReadResult.TotalRawRows,
            Rows = rows
        };
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
        }

        return sharedStrings;
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
}
