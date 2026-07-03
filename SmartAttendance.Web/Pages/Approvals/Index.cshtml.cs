using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Approvals;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Step { get; set; } = "Manager";

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "Pending";

    [BindProperty]
    public string? Note { get; set; }

    public List<ApprovalRow> Requests { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string step)
    {
        await ReviewAsync(id, step, "Approved");
        Message = step == "Manager" ? "تمت موافقة المدير وتحويل الطلب إلى HR." : "تم اعتماد الطلب نهائياً.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string step)
    {
        await ReviewAsync(id, step, "Rejected");
        Message = "تم رفض الطلب.";
        await LoadAsync();
        return Page();
    }

    private async Task ReviewAsync(int id, string step, string action)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var normalizedStep = step == "HR" ? "HR" : "Manager";

        string sql;

        if (normalizedStep == "Manager")
        {
            sql = action == "Approved"
                ? """
UPDATE SelfServiceRequests
SET ManagerStatus = 'Approved',
    ManagerReviewedBy = 'Direct Manager',
    ManagerReviewedAt = SYSUTCDATETIME(),
    ManagerNote = @Note,
    CurrentStep = 'HR',
    Status = 'Pending',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@Id, 'Direct Manager', 'Approved', 'Direct Manager', @Note);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب بانتظار HR', N'تمت موافقة المدير ويوجد طلب بانتظار HR', 'HR', '/Approvals?Step=HR');
"""
                : """
UPDATE SelfServiceRequests
SET ManagerStatus = 'Rejected',
    ManagerReviewedBy = 'Direct Manager',
    ManagerReviewedAt = SYSUTCDATETIME(),
    ManagerNote = @Note,
    CurrentStep = 'Rejected',
    Status = 'Rejected',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@Id, 'Direct Manager', 'Rejected', 'Direct Manager', @Note);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مرفوض', N'تم رفض طلب خدمة ذاتية من المدير', 'Employee', '/SelfServices');
""";
        }
        else
        {
            sql = action == "Approved"
                ? """
UPDATE SelfServiceRequests
SET HrStatus = 'Approved',
    HrReviewedBy = 'HR',
    HrReviewedAt = SYSUTCDATETIME(),
    HrNote = @Note,
    CurrentStep = 'Completed',
    Status = 'Approved',
    ReviewedBy = 'HR',
    ReviewNote = @Note,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@Id, 'HR', 'Approved', 'HR', @Note);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب معتمد', N'تم اعتماد طلب خدمة ذاتية نهائياً', 'Employee', '/SelfServices');
"""
                : """
UPDATE SelfServiceRequests
SET HrStatus = 'Rejected',
    HrReviewedBy = 'HR',
    HrReviewedAt = SYSUTCDATETIME(),
    HrNote = @Note,
    CurrentStep = 'Rejected',
    Status = 'Rejected',
    ReviewedBy = 'HR',
    ReviewNote = @Note,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@Id, 'HR', 'Rejected', 'HR', @Note);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مرفوض', N'تم رفض طلب خدمة ذاتية من HR', 'Employee', '/SelfServices');
""";
        }

        sql += """

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('SelfServiceRequest', CAST(@Id AS nvarchar(80)), @AuditAction, @Note, @UserName, @IpAddress);
""";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Note", Note);
                HrmsDatabase.AddParameter(command, "@AuditAction", $"{normalizedStep} {action}");
                HrmsDatabase.AddParameter(command, "@UserName", normalizedStep == "HR" ? "HR" : "Direct Manager");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });
    }

    private async Task LoadAsync()
    {
        var currentStep = Step == "HR" ? "HR" : "Direct Manager";

        var sql = """
SELECT TOP 200
    r.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(m.FullName, '') AS DirectManager,
    r.RequestType,
    r.RequestDate,
    r.FromDate,
    r.ToDate,
    CONVERT(varchar(5), r.StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), r.EndTime, 108) AS EndTimeText,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.ManagerStatus, '') AS ManagerStatus,
    ISNULL(r.HrStatus, '') AS HrStatus,
    ISNULL(r.Reason, '') AS Reason,
    r.CreatedAt
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE
    (@Status = 'All' OR r.Status = @Status)
    AND (@Step = 'All' OR r.CurrentStep = @Step)
ORDER BY r.CreatedAt DESC;
""";

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", Status);
                HrmsDatabase.AddParameter(command, "@Step", Step == "All" ? "All" : currentStep);
            },
            reader => new ApprovalRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestDate = HrmsDatabase.GetDateOnly(reader, "RequestDate"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                ManagerStatus = HrmsDatabase.GetString(reader, "ManagerStatus"),
                HrStatus = HrmsDatabase.GetString(reader, "HrStatus"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public class ApprovalRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string DirectManager { get; set; } = string.Empty;

        public string RequestType { get; set; } = string.Empty;

        public DateOnly? RequestDate { get; set; }

        public DateOnly? FromDate { get; set; }

        public DateOnly? ToDate { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CurrentStep { get; set; } = string.Empty;

        public string ManagerStatus { get; set; } = string.Empty;

        public string HrStatus { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}
