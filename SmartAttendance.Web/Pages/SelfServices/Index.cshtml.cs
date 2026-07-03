using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.SelfServices;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public RequestInput Input { get; set; } = new();

    public List<EmployeeOption> Employees { get; set; } = new();

    public List<RequestRow> Requests { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, RequestDate, FromDate, ToDate, StartTime, EndTime, Reason, Status, CurrentStep, CreatedBy)
VALUES
(@EmployeeId, @RequestType, @RequestDate, @FromDate, @ToDate, @StartTime, @EndTime, @Reason, 'Pending', 'Direct Manager', 'Employee');

DECLARE @RequestId int = SCOPE_IDENTITY();

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, 'Submission', 'Submitted', 'Employee', @Reason);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب خدمة ذاتية جديد', N'يوجد طلب جديد بانتظار الموافقة', 'HR', '/Approvals');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", Input.RequestType);
                HrmsDatabase.AddParameter(command, "@RequestDate", Input.RequestDate);
                HrmsDatabase.AddParameter(command, "@FromDate", Input.FromDate);
                HrmsDatabase.AddParameter(command, "@ToDate", Input.ToDate);
                HrmsDatabase.AddParameter(command, "@StartTime", Input.StartTime);
                HrmsDatabase.AddParameter(command, "@EndTime", Input.EndTime);
                HrmsDatabase.AddParameter(command, "@Reason", Input.Reason);
            });

        Message = "تم إرسال الطلب للموافقة.";
        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        Employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 500 Id, EmployeeNo, FullName FROM Employees ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 100
    r.Id,
    e.EmployeeNo,
    e.FullName,
    r.RequestType,
    r.RequestDate,
    r.FromDate,
    r.ToDate,
    CONVERT(varchar(5), r.StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), r.EndTime, 108) AS EndTimeText,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.Reason, '') AS Reason,
    r.CreatedAt
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
ORDER BY r.CreatedAt DESC;
""",
            null,
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestDate = HrmsDatabase.GetDateOnly(reader, "RequestDate"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public class RequestInput
    {
        public int EmployeeId { get; set; }

        public string RequestType { get; set; } = "Leave";

        public DateOnly? RequestDate { get; set; }

        public DateOnly? FromDate { get; set; }

        public DateOnly? ToDate { get; set; }

        public TimeOnly? StartTime { get; set; }

        public TimeOnly? EndTime { get; set; }

        public string? Reason { get; set; }
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    public class RequestRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string RequestType { get; set; } = string.Empty;

        public DateOnly? RequestDate { get; set; }

        public DateOnly? FromDate { get; set; }

        public DateOnly? ToDate { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CurrentStep { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}
