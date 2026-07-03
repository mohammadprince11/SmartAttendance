using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceCorrections;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty]
    public CorrectionInput Input { get; set; } = new();

    public List<AttendanceRow> Records { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostCorrectAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var oldRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    Id,
    AttendanceDate,
    CheckIn,
    CheckOut,
    Status,
    ISNULL(Notes, '') AS Notes
FROM AttendanceRecords
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", Input.Id),
            reader => new AttendanceRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate")?.ToString("yyyy-MM-dd") ?? "",
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm") ?? "",
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm") ?? "",
                Status = HrmsDatabase.GetString(reader, "Status"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });

        var old = oldRows.FirstOrDefault();

        if (old == null || string.IsNullOrWhiteSpace(old.Date))
        {
            Message = "السجل غير موجود.";
            await LoadAsync();
            return Page();
        }

        var date = DateOnly.Parse(old.Date);
        var checkIn = BuildDateTime(date, Input.CheckIn);
        DateTime? checkOut = string.IsNullOrWhiteSpace(Input.CheckOut) ? null : BuildDateTime(date, Input.CheckOut);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE AttendanceRecords
SET CheckIn = @CheckIn,
    CheckOut = @CheckOut,
    Status = @Status,
    Notes = @Notes
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
VALUES ('AttendanceRecord', CAST(@Id AS nvarchar(80)), 'Attendance Correction', @OldValues, @NewValues, 'HR', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Input.Id);
                HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                HrmsDatabase.AddParameter(command, "@Status", Input.Status);
                HrmsDatabase.AddParameter(command, "@Notes", Input.Notes);
                HrmsDatabase.AddParameter(command, "@OldValues", HrmsDatabase.JsonLine(
                    ("CheckIn", old.CheckIn),
                    ("CheckOut", old.CheckOut),
                    ("Status", old.Status),
                    ("Notes", old.Notes)));
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("CheckIn", Input.CheckIn),
                    ("CheckOut", Input.CheckOut),
                    ("Status", Input.Status),
                    ("Notes", Input.Notes)));
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        Message = "تم تصحيح سجل الحضور وتسجيل العملية في Audit Log.";
        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        var sql = """
SELECT TOP 100
    ar.Id,
    e.EmployeeNo,
    e.FullName,
    ar.AttendanceDate,
    ar.CheckIn,
    ar.CheckOut,
    ar.Status,
    ar.Source,
    ISNULL(ar.Notes, '') AS Notes
FROM AttendanceRecords ar
INNER JOIN Employees e ON ar.EmployeeId = e.Id
WHERE
    (@EmployeeNo IS NULL OR @EmployeeNo = '' OR e.EmployeeNo LIKE '%' + @EmployeeNo + '%')
    AND (@FromDate IS NULL OR ar.AttendanceDate >= @FromDate)
    AND (@ToDate IS NULL OR ar.AttendanceDate <= @ToDate)
ORDER BY ar.AttendanceDate DESC, ar.CheckIn DESC;
""";

        Records = await HrmsDatabase.QueryAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeNo", EmployeeNo);
                HrmsDatabase.AddParameter(command, "@FromDate", FromDate);
                HrmsDatabase.AddParameter(command, "@ToDate", ToDate);
            },
            reader => new AttendanceRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate")?.ToString("yyyy-MM-dd") ?? "",
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm") ?? "",
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm") ?? "",
                Status = HrmsDatabase.GetString(reader, "Status"),
                Source = HrmsDatabase.GetString(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });
    }

    private static DateTime BuildDateTime(DateOnly date, string? time)
    {
        var parsed = TimeOnly.TryParse(time, out var timeOnly)
            ? timeOnly
            : new TimeOnly(0, 0);

        return date.ToDateTime(parsed);
    }

    public class CorrectionInput
    {
        public int Id { get; set; }

        public string? CheckIn { get; set; }

        public string? CheckOut { get; set; }

        public string Status { get; set; } = "Present";

        public string? Notes { get; set; }
    }

    public class AttendanceRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string Date { get; set; } = string.Empty;

        public string CheckIn { get; set; } = string.Empty;

        public string CheckOut { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
    }
}
