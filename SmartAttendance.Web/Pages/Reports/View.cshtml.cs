using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Reports;

public class ViewModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ViewModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Fields { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Name { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SystemKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Branch { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public string ReportTitle => string.IsNullOrWhiteSpace(Name) ? "تقرير" : Name;

    public List<ReportField> SelectedFields { get; set; } = new();

    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public List<string> Branches { get; set; } = new();

    public List<string> Departments { get; set; } = new();

    public int TotalRows { get; set; }

    public int TotalPages { get; set; }

    public int StartRow { get; set; }

    public int EndRow { get; set; }

    public bool IsLimited { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizePaging();
        await BuildReportAsync(exportMode: false);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        NormalizePaging();
        await BuildReportAsync(exportMode: true);

        var headers = SelectedFields.Select(x => x.LabelAr).ToList();
        var dataRows = Rows
            .Select(row => SelectedFields.Select(field => row.TryGetValue(field.Key, out var value) ? value : string.Empty).ToList())
            .ToList();

        var bytes = SimpleXlsxBuilder.Build(headers, dataRows);
        var safeName = MakeSafeFileName(ReportTitle);
        var fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private void NormalizePaging()
    {
        PageSize = PageSize switch
        {
            10 => 10,
            25 => 25,
            50 => 50,
            100 => 100,
            _ => 25
        };

        if (PageNumber < 1)
        {
            PageNumber = 1;
        }
    }

    private async Task BuildReportAsync(bool exportMode)
    {
        Source = NormalizeSource(Source);
        SelectedFields = ResolveSelectedFields(Source, Fields);

        Branches = await _dbContext.Branches
            .AsNoTracking()
            .Select(x => x.Name)
            .Where(x => x != "")
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        Departments = await _dbContext.Departments
            .AsNoTracking()
            .Select(x => x.Name)
            .Where(x => x != "")
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        if (Source == "attendance")
        {
            await BuildAttendanceRowsAsync(exportMode);
        }
        else
        {
            await BuildEmployeeRowsAsync(exportMode);
        }

        TotalPages = TotalRows <= 0
            ? 1
            : (int)Math.Ceiling(TotalRows / (double)PageSize);

        if (!exportMode && PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        StartRow = TotalRows == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
        EndRow = TotalRows == 0 ? 0 : Math.Min(StartRow + Rows.Count - 1, TotalRows);
    }

    private async Task BuildEmployeeRowsAsync(bool exportMode)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Include(x => x.Department)
            .ThenInclude(x => x.Branch)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();

            query = query.Where(x =>
                x.EmployeeNo.Contains(search) ||
                x.FullName.Contains(search) ||
                (x.Phone != null && x.Phone.Contains(search)) ||
                (x.Email != null && x.Email.Contains(search)) ||
                x.Department.Name.Contains(search) ||
                x.Department.Branch.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(Branch))
        {
            query = query.Where(x => x.Department.Branch.Name == Branch);
        }

        if (!string.IsNullOrWhiteSpace(Department))
        {
            query = query.Where(x => x.Department.Name == Department);
        }

        if (FromDate.HasValue)
        {
            query = query.Where(x => x.HireDate >= FromDate.Value);
        }

        if (ToDate.HasValue)
        {
            query = query.Where(x => x.HireDate <= ToDate.Value);
        }

        query = SystemKey == "new-joiners"
            ? query.OrderByDescending(x => x.HireDate).ThenBy(x => x.EmployeeNo)
            : query.OrderBy(x => x.EmployeeNo);

        TotalRows = await query.CountAsync();

        var exportLimit = 20000;
        IsLimited = exportMode && TotalRows > exportLimit;

        var employeesQuery = exportMode
            ? query.Take(exportLimit)
            : query.Skip((PageNumber - 1) * PageSize).Take(PageSize);

        var employees = await employeesQuery.ToListAsync();

        Rows = employees.Select(BuildEmployeeRow).ToList();
    }

    private async Task BuildAttendanceRowsAsync(bool exportMode)
    {
        var query = _dbContext.AttendanceRecords
            .AsNoTracking()
            .Include(x => x.Employee)
            .ThenInclude(x => x.Department)
            .ThenInclude(x => x.Branch)
            .Include(x => x.Device)
            .AsQueryable();

        if (FromDate.HasValue)
        {
            query = query.Where(x => x.AttendanceDate >= FromDate.Value);
        }

        if (ToDate.HasValue)
        {
            query = query.Where(x => x.AttendanceDate <= ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();

            query = query.Where(x =>
                x.Employee.EmployeeNo.Contains(search) ||
                x.Employee.FullName.Contains(search) ||
                x.Employee.Department.Name.Contains(search) ||
                x.Employee.Department.Branch.Name.Contains(search) ||
                (x.Device != null && x.Device.Name.Contains(search)) ||
                (x.Notes != null && x.Notes.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(Branch))
        {
            query = query.Where(x => x.Employee.Department.Branch.Name == Branch);
        }

        if (!string.IsNullOrWhiteSpace(Department))
        {
            query = query.Where(x => x.Employee.Department.Name == Department);
        }

        if (SystemKey == "late-employees")
        {
            query = query.Where(x => x.Status == AttendanceStatus.Late);
        }
        else if (SystemKey == "absent-employees")
        {
            query = query.Where(x => x.Status == AttendanceStatus.Absent);
        }
        else if (SystemKey == "missing-punch")
        {
            query = query.Where(x => x.CheckOut == null);
        }

        query = query
            .OrderByDescending(x => x.AttendanceDate)
            .ThenByDescending(x => x.CheckIn);

        TotalRows = await query.CountAsync();

        var exportLimit = 20000;
        IsLimited = exportMode && TotalRows > exportLimit;

        var recordsQuery = exportMode
            ? query.Take(exportLimit)
            : query.Skip((PageNumber - 1) * PageSize).Take(PageSize);

        var records = await recordsQuery.ToListAsync();

        Rows = records.Select(BuildAttendanceRow).ToList();
    }

    private Dictionary<string, string> BuildEmployeeRow(Employee employee)
    {
        return new Dictionary<string, string>
        {
            ["EmployeeCode"] = employee.EmployeeNo,
            ["EmployeeName"] = employee.FullName,
            ["Branch"] = employee.Department?.Branch?.Name ?? string.Empty,
            ["Department"] = employee.Department?.Name ?? string.Empty,
            ["Position"] = ReadStringProperty(employee, "Position"),
            ["HireDate"] = employee.HireDate.ToString("yyyy-MM-dd"),
            ["Active"] = employee.IsActive ? "Yes" : "No",
            ["Phone"] = employee.Phone ?? string.Empty,
            ["Email"] = employee.Email ?? string.Empty,
            ["NationalId"] = employee.NationalId ?? string.Empty
        };
    }

    private Dictionary<string, string> BuildAttendanceRow(AttendanceRecord record)
    {
        return new Dictionary<string, string>
        {
            ["EmployeeCode"] = record.Employee?.EmployeeNo ?? string.Empty,
            ["EmployeeName"] = record.Employee?.FullName ?? string.Empty,
            ["Date"] = record.AttendanceDate.ToString("yyyy-MM-dd"),
            ["CheckIn"] = record.CheckIn.ToString("HH:mm"),
            ["CheckOut"] = record.CheckOut?.ToString("HH:mm") ?? string.Empty,
            ["Status"] = record.Status.ToString(),
            ["Source"] = record.Source.ToString(),
            ["Device"] = record.Device?.Name ?? string.Empty,
            ["Branch"] = record.Employee?.Department?.Branch?.Name ?? string.Empty,
            ["Department"] = record.Employee?.Department?.Name ?? string.Empty
        };
    }

    private static string NormalizeSource(string? source)
    {
        return string.Equals(source, "attendance", StringComparison.OrdinalIgnoreCase)
            ? "attendance"
            : "employees";
    }

    private static List<ReportField> ResolveSelectedFields(string source, string? fields)
    {
        var definitions = GetFieldDefinitions(source);
        var keys = (fields ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!keys.Any())
        {
            keys = source == "attendance"
                ? new List<string> { "EmployeeCode", "EmployeeName", "Date", "CheckIn", "CheckOut", "Status" }
                : new List<string> { "EmployeeCode", "EmployeeName", "Branch", "Department", "HireDate", "Active" };
        }

        var result = new List<ReportField>();

        foreach (var key in keys)
        {
            var match = definitions.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                result.Add(match);
            }
        }

        return result.Any() ? result : definitions.Take(6).ToList();
    }

    private static List<ReportField> GetFieldDefinitions(string source)
    {
        if (source == "attendance")
        {
            return new List<ReportField>
            {
                new("EmployeeCode", "رقم الموظف", "Employee Code"),
                new("EmployeeName", "اسم الموظف", "Employee Name"),
                new("Date", "التاريخ", "Date"),
                new("CheckIn", "دخول", "Check In"),
                new("CheckOut", "خروج", "Check Out"),
                new("Status", "الحالة", "Status"),
                new("Source", "المصدر", "Source"),
                new("Device", "الجهاز", "Device"),
                new("Branch", "الفرع", "Branch"),
                new("Department", "القسم", "Department")
            };
        }

        return new List<ReportField>
        {
            new("EmployeeCode", "رقم الموظف", "Employee Code"),
            new("EmployeeName", "اسم الموظف", "Employee Name"),
            new("Branch", "الفرع", "Branch"),
            new("Department", "القسم", "Department"),
            new("Position", "المنصب", "Position"),
            new("HireDate", "تاريخ المباشرة", "Hire Date"),
            new("Active", "فعال", "Active"),
            new("Phone", "الهاتف", "Phone"),
            new("Email", "البريد الإلكتروني", "Email"),
            new("NationalId", "الرقم الوطني", "National ID")
        };
    }

    private static string ReadStringProperty(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName);

        if (property == null)
        {
            return string.Empty;
        }

        return property.GetValue(source)?.ToString() ?? string.Empty;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

        return string.IsNullOrWhiteSpace(safe) ? "Report" : safe;
    }

    public record ReportField(string Key, string LabelAr, string LabelEn);

    private static class SimpleXlsxBuilder
    {
        public static byte[] Build(List<string> headers, List<List<string>> rows)
        {
            using var memoryStream = new MemoryStream();

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                AddEntry(archive, "[Content_Types].xml", ContentTypesXml());
                AddEntry(archive, "_rels/.rels", RootRelationshipsXml());
                AddEntry(archive, "xl/workbook.xml", WorkbookXml());
                AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
                AddEntry(archive, "xl/styles.xml", StylesXml());
                AddEntry(archive, "xl/worksheets/sheet1.xml", WorksheetXml(headers, rows));
            }

            return memoryStream.ToArray();
        }

        private static void AddEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);

            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            writer.Write(content);
        }

        private static string WorksheetXml(List<string> headers, List<List<string>> rows)
        {
            var builder = new StringBuilder();

            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            builder.Append("<sheetData>");

            builder.Append("<row r=\"1\">");

            for (var column = 0; column < headers.Count; column++)
            {
                AppendCell(builder, column, 1, headers[column]);
            }

            builder.Append("</row>");

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var excelRow = rowIndex + 2;
                builder.Append("<row r=\"").Append(excelRow).Append("\">");

                for (var column = 0; column < headers.Count; column++)
                {
                    var value = column < rows[rowIndex].Count ? rows[rowIndex][column] : string.Empty;
                    AppendCell(builder, column, excelRow, value);
                }

                builder.Append("</row>");
            }

            builder.Append("</sheetData>");
            builder.Append("</worksheet>");

            return builder.ToString();
        }

        private static void AppendCell(StringBuilder builder, int column, int row, string value)
        {
            var cellReference = GetCellReference(column, row);
            var escaped = EscapeXml(value);

            builder
                .Append("<c r=\"")
                .Append(cellReference)
                .Append("\" t=\"inlineStr\"><is><t>")
                .Append(escaped)
                .Append("</t></is></c>");
        }

        private static string GetCellReference(int zeroBasedColumn, int row)
        {
            var dividend = zeroBasedColumn + 1;
            var columnName = string.Empty;

            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }

            return $"{columnName}{row}";
        }

        private static string EscapeXml(string? value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string ContentTypesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                   "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                   "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                   "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                   "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                   "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                   "</Types>";
        }

        private static string RootRelationshipsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private static string WorkbookXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets>" +
                   "<sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/>" +
                   "</sheets>" +
                   "</workbook>";
        }

        private static string WorkbookRelationshipsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                   "</Relationships>";
        }

        private static string StylesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                   "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                   "</styleSheet>";
        }
    }
}
