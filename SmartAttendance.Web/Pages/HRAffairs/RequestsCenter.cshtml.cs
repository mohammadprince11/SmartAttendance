using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HRAffairs;

public class RequestsCenterModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public RequestsCenterModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; } = "Pending";

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 200;

    public List<RequestCenterRow> Rows { get; set; } = new();

    public int PendingCount => Rows.Count(x => x.Status == "Pending");
    public int ApprovedCount => Rows.Count(x => x.Status == "Approved");
    public int RejectedCount => Rows.Count(x => x.Status == "Rejected");

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        MaxRows = MaxRows <= 0 ? 200 : Math.Min(MaxRows, 1000);
        var status = string.IsNullOrWhiteSpace(StatusFilter) ? "" : StatusFilter.Trim();

        Rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP (@MaxRows)
    r.Id,
    r.EmployeeId,
    e.EmployeeNo,
    e.FullName,
    ISNULL(b.Name, '') AS BranchName,
    r.RequestType,
    r.RequestDate,
    r.FromDate,
    r.ToDate,
    ISNULL(r.Status, '') AS Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.Reason, '') AS Reason,
    r.CreatedAt
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE
  (
      @StatusFilter = ''
      OR r.Status = @StatusFilter
  )
  AND
  (
      @SearchTerm = ''
      OR e.EmployeeNo LIKE '%' + @SearchTerm + '%'
      OR e.FullName LIKE '%' + @SearchTerm + '%'
      OR r.RequestType LIKE '%' + @SearchTerm + '%'
      OR r.Status LIKE '%' + @SearchTerm + '%'
  )
ORDER BY r.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@StatusFilter", status == "all" ? "" : status);
                HrmsDatabase.AddParameter(command, "@SearchTerm", string.IsNullOrWhiteSpace(SearchTerm) ? "" : SearchTerm.Trim());
                HrmsDatabase.AddParameter(command, "@MaxRows", MaxRows);
            },
            reader => new RequestCenterRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestDate = HrmsDatabase.GetDateOnly(reader, "RequestDate"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public string DisplayDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "-";
    public string DisplayDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "-";

    public string StatusClass(string status)
    {
        return status switch
        {
            "Approved" => "ok",
            "Rejected" => "danger",
            "Pending" => "warn",
            _ => "warn"
        };
    }

    public string StatusName(string status)
    {
        return status switch
        {
            "Approved" => "موافق عليه",
            "Rejected" => "مرفوض",
            "Pending" => "معلق",
            _ => status
        };
    }

    public class RequestCenterRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string RequestType { get; set; } = "";
        public DateOnly? RequestDate { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string Status { get; set; } = "";
        public string CurrentStep { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
    }
}
