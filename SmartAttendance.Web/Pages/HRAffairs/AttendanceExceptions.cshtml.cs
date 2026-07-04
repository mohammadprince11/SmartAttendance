using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HRAffairs;

public class AttendanceExceptionsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public AttendanceExceptionsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 200;

    public List<ExceptionRow> Rows { get; set; } = new();

    public int TotalRows => Rows.Count;
    public int LateCount => Rows.Count(x => x.Status == 2);
    public int AbsentCount => Rows.Count(x => x.Status == 3);
    public int MissingPunchCount => Rows.Count(x => !x.CheckIn.HasValue || !x.CheckOut.HasValue);

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        FromDate ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        ToDate ??= DateOnly.FromDateTime(DateTime.Today);
        MaxRows = MaxRows <= 0 ? 200 : Math.Min(MaxRows, 1000);

        Rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP (@MaxRows)
    e.Id AS EmployeeId,
    e.EmployeeNo,
    e.FullName,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ar.AttendanceDate,
    ar.CheckIn,
    ar.CheckOut,
    ar.Status,
    ar.Source,
    ISNULL(ar.Notes, '') AS Notes
FROM AttendanceRecords ar
INNER JOIN Employees e ON ar.EmployeeId = e.Id
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE ar.AttendanceDate BETWEEN @FromDate AND @ToDate
  AND
  (
      ar.Status IN (2,3)
      OR ar.CheckIn IS NULL
      OR ar.CheckOut IS NULL
      OR ISNULL(ar.Notes, '') <> ''
  )
  AND
  (
      @SearchTerm = ''
      OR e.EmployeeNo LIKE '%' + @SearchTerm + '%'
      OR e.FullName LIKE '%' + @SearchTerm + '%'
      OR ISNULL(d.Name, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(b.Name, '') LIKE '%' + @SearchTerm + '%'
  )
ORDER BY ar.AttendanceDate DESC, e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FromDate", FromDate.Value.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", ToDate.Value.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@SearchTerm", string.IsNullOrWhiteSpace(SearchTerm) ? "" : SearchTerm.Trim());
                HrmsDatabase.AddParameter(command, "@MaxRows", MaxRows);
            },
            reader => new ExceptionRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                Status = HrmsDatabase.GetInt(reader, "Status"),
                Source = HrmsDatabase.GetInt(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });
    }

    public string DisplayDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "-";
    public string DisplayTime(DateTime? value) => value?.ToString("HH:mm") ?? "-";

    public string StatusName(int status)
    {
        return status switch
        {
            1 => "حاضر",
            2 => "متأخر",
            3 => "غياب",
            4 => "بصمة ناقصة",
            5 => "عطلة",
            _ => "غير محدد"
        };
    }

    public string StatusClass(int status)
    {
        return status switch
        {
            1 => "ok",
            2 => "warn",
            3 => "danger",
            4 => "warn",
            _ => "warn"
        };
    }

    public string SourceName(int source)
    {
        return source switch
        {
            1 => "جهاز بصمة",
            2 => "إدخال يدوي",
            3 => "استيراد",
            4 => "طلب",
            5 => "نظام",
            _ => "غير محدد"
        };
    }

    public class ExceptionRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public DateOnly? AttendanceDate { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public int Status { get; set; }
        public int Source { get; set; }
        public string Notes { get; set; } = "";
    }
}
