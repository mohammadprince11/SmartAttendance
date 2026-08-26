using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Reports;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Approvals;

/// <summary>تقرير تشغيلي، محصور بنطاق شركة المستخدم، على مصدر طلبات الموافقات الفعلي.</summary>
public sealed class ReportsModel : PageModel
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "All", "Pending", "Approved", "Rejected"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public ReportsModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "All";
    [BindProperty(SupportsGet = true)] public string Source { get; set; } = "All";
    [BindProperty(SupportsGet = true)] public string? RequestType { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public Summary Totals { get; private set; } = new();
    public List<TypeSummary> ByType { get; private set; } = new();
    public List<RequestRow> Requests { get; private set; } = new();
    public List<string> RequestTypes { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        await LoadAsync();
        var columns = new List<ReportExportService.Column>
        {
            new("Id", "رقم الطلب"), new("EmployeeNo", "الرقم الوظيفي"),
            new("EmployeeName", "الموظف"), new("Department", "القسم"),
            new("RequestType", "نوع الطلب"), new("Status", "الحالة"),
            new("CurrentStep", "الخطوة الحالية"), new("CreatedAt", "تاريخ الطلب"),
            new("UpdatedAt", "آخر حركة"), new("TurnaroundHours", "ساعات المعالجة")
        };
        var rows = Requests.Select(request => new Dictionary<string, string>
        {
            ["Id"] = request.Id.ToString(),
            ["EmployeeNo"] = request.EmployeeNo,
            ["EmployeeName"] = request.EmployeeName,
            ["Department"] = request.Department,
            ["RequestType"] = DisplayType(request.RequestType),
            ["Status"] = DisplayStatus(request.Status),
            ["CurrentStep"] = request.CurrentStep,
            ["CreatedAt"] = request.CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            ["UpdatedAt"] = request.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            ["TurnaroundHours"] = request.TurnaroundHours?.ToString("0.0") ?? string.Empty
        }).ToList();
        var export = ReportExportService.Build(format, "تقرير الموافقات", columns, rows);
        return File(export.Bytes, export.ContentType, $"approval-report-{DateTime.UtcNow:yyyyMMdd}.{export.Extension}");
    }

    private async Task LoadAsync()
    {
        var requestedStatus = Status ?? string.Empty;
        Status = AllowedStatuses.Contains(requestedStatus) ? requestedStatus : "All";
        Source = Source is "SelfService" or "Admin" or "Legacy" ? Source : "All";
        if (From.HasValue && To.HasValue && From > To) (From, To) = (To, From);

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var filter = $"""
WHERE {scopeFilter}
  AND (@Status = 'All' OR ISNULL(r.Status,'Pending') = @Status)
  AND (@Source = 'All' OR r.RequestSource = @Source)
  AND (@RequestType IS NULL OR r.RequestType = @RequestType)
  AND (@Search IS NULL OR e.FullName LIKE '%' + @Search + '%' OR e.EmployeeNo LIKE '%' + @Search + '%')
  AND (@From IS NULL OR CAST(r.CreatedAt AS date) >= @From)
  AND (@To IS NULL OR CAST(r.CreatedAt AS date) <= @To)
""";

        void Parameters(System.Data.Common.DbCommand command)
        {
            HrmsDatabase.AddParameter(command, "@Status", Status);
            HrmsDatabase.AddParameter(command, "@Source", Source);
            HrmsDatabase.AddParameter(command, "@RequestType", DbValue(RequestType));
            HrmsDatabase.AddParameter(command, "@Search", DbValue(Search));
            HrmsDatabase.AddParameter(command, "@From", DateValue(From));
            HrmsDatabase.AddParameter(command, "@To", DateValue(To));
        }

        Totals = (await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT COUNT(*) AS Total,
       SUM(CASE WHEN ISNULL(r.Status,'Pending')='Pending' THEN 1 ELSE 0 END) AS Pending,
       SUM(CASE WHEN r.Status='Approved' THEN 1 ELSE 0 END) AS Approved,
       SUM(CASE WHEN r.Status='Rejected' THEN 1 ELSE 0 END) AS Rejected,
       SUM(CASE WHEN ISNULL(r.Status,'Pending')='Pending' AND DATEDIFF(HOUR,r.CreatedAt,SYSUTCDATETIME()) >= 48 THEN 1 ELSE 0 END) AS OlderThan48Hours,
       CAST(AVG(CASE WHEN r.Status IN ('Approved','Rejected') AND r.UpdatedAt IS NOT NULL
                     THEN CAST(DATEDIFF(MINUTE,r.CreatedAt,r.UpdatedAt) AS decimal(18,2)) / 60 END) AS decimal(18,2)) AS AverageHours
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id=r.EmployeeId
{filter};
""",
            Parameters,
            reader => new Summary
            {
                Total = HrmsDatabase.GetInt(reader, "Total"),
                Pending = HrmsDatabase.GetInt(reader, "Pending"),
                Approved = HrmsDatabase.GetInt(reader, "Approved"),
                Rejected = HrmsDatabase.GetInt(reader, "Rejected"),
                OlderThan48Hours = HrmsDatabase.GetInt(reader, "OlderThan48Hours"),
                AverageHours = HrmsDatabase.GetNullableDecimal(reader, "AverageHours")
            })).Single();

        ByType = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT r.RequestType,
       COUNT(*) AS Total,
       SUM(CASE WHEN ISNULL(r.Status,'Pending')='Pending' THEN 1 ELSE 0 END) AS Pending,
       SUM(CASE WHEN r.Status='Approved' THEN 1 ELSE 0 END) AS Approved,
       SUM(CASE WHEN r.Status='Rejected' THEN 1 ELSE 0 END) AS Rejected,
       CAST(AVG(CASE WHEN r.Status IN ('Approved','Rejected') AND r.UpdatedAt IS NOT NULL
                     THEN CAST(DATEDIFF(MINUTE,r.CreatedAt,r.UpdatedAt) AS decimal(18,2)) / 60 END) AS decimal(18,2)) AS AverageHours
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id=r.EmployeeId
{filter}
GROUP BY r.RequestType
ORDER BY Total DESC, r.RequestType;
""",
            Parameters,
            reader => new TypeSummary
            {
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                Total = HrmsDatabase.GetInt(reader, "Total"),
                Pending = HrmsDatabase.GetInt(reader, "Pending"),
                Approved = HrmsDatabase.GetInt(reader, "Approved"),
                Rejected = HrmsDatabase.GetInt(reader, "Rejected"),
                AverageHours = HrmsDatabase.GetNullableDecimal(reader, "AverageHours")
            });

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT TOP 300 r.Id,e.EmployeeNo,e.FullName,ISNULL(d.Name,'') AS Department,
       r.RequestType,ISNULL(r.Status,'Pending') AS Status,ISNULL(r.CurrentStep,'') AS CurrentStep,
       r.CreatedAt,r.UpdatedAt,
       CASE WHEN r.UpdatedAt IS NULL THEN NULL
            ELSE CAST(DATEDIFF(MINUTE,r.CreatedAt,r.UpdatedAt) AS decimal(18,2)) / 60 END AS TurnaroundHours
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id=r.EmployeeId
LEFT JOIN Departments d ON d.Id=e.DepartmentId
{filter}
ORDER BY r.CreatedAt DESC;
""",
            Parameters,
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Department = HrmsDatabase.GetString(reader, "Department"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                UpdatedAt = HrmsDatabase.GetDateTime(reader, "UpdatedAt"),
                TurnaroundHours = HrmsDatabase.GetNullableDecimal(reader, "TurnaroundHours")
            });

        RequestTypes = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT DISTINCT r.RequestType
FROM SelfServiceRequests r INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE {scopeFilter} AND ISNULL(r.RequestType,'')<>'' ORDER BY r.RequestType;
""",
            null,
            reader => HrmsDatabase.GetString(reader, "RequestType"));
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object DateValue(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value;

    public static string DisplayStatus(string status) => status switch
    {
        "Pending" => "قيد الموافقة", "Approved" => "معتمد", "Rejected" => "مرفوض", _ => status
    };

    public static string DisplayType(string type) => type switch
    {
        "Leave" or "إجازة" => "إجازة",
        "MissingPunch" or "نسيان بصمة" => "نسيان بصمة",
        "ExitPermission" or "خروج أثناء الدوام" => "مغادرة",
        "Overtime" or "عمل إضافي" => "عمل إضافي",
        "ShiftRequest" or "طلب مناوبة" => "طلب مناوبة",
        _ => type
    };

    public class Summary
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public int OlderThan48Hours { get; set; }
        public decimal? AverageHours { get; set; }
    }

    public sealed class TypeSummary : Summary
    {
        public string RequestType { get; set; } = string.Empty;
    }

    public sealed class RequestRow
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public decimal? TurnaroundHours { get; set; }
    }
}
