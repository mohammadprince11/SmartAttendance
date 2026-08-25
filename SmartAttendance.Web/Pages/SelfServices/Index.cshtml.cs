using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.SelfServices;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext dbContext,
        ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty]
    public RequestInput Input { get; set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — يبدأ بهما <c>_EmployeePicker</c> معبَّأً.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

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

        // لا نعتمد EmployeeId القادم من النموذج بلا فحص: لا يُنشأ طلبٌ إلا لموظفٍ ضمن
        // شركات المستخدم، وإلا كان حقنَ بياناتٍ (وسريان موافقات) على موظف شركةٍ أخرى.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _dbContext, Input.EmployeeId, scope, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        var requestId = await HrmsDatabase.ScalarAsync<int>(
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

SELECT @RequestId;
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

        // سريان الموافقات: حلّ القالب المناسب وتجميد خطوات اللجنة على الطلب.
        if (requestId > 0)
        {
            var start=await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, Input.RequestType, Input.EmployeeId);
            if(!start.Ok) Message=start.Message;
        }

        Message ??= "تم إرسال الطلب للموافقة.";
        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        // المنتقي لا يحمّل قائمة — صفٌّ واحد للموظف المحسوم إن وُجد.
        var identity = await Shared.EmployeePickerLookup.LoadAsync(_dbContext, Input.EmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;

        // AUTHZ-005: كان هذا السرد **بلا أي فلتر** — آخر 100 طلبٍ من كل الشركات
        // بأسماء أصحابها وأسباب طلباتهم، ويبلغه أدنى دور (الحارس يمرّر كل GET
        // على /selfservices). الكتابة كانت محروسة أعلاه؛ القراءة وحدها مكشوفة.
        //
        // بُعدان يُفرضان معاً كنمط LeaveRequestAccessScope:
        //   (١) الشركة — بالشرط الذي يبنيه EmployeeCompanyGuard نفسه.
        //   (٢) الموظّف — دور «موظف» لا يرى إلا طلباته هو.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var companyPredicate = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");

        var ownEmployeeId = ResolveOwnEmployeeIdForEmployeeRole();

        // مغلق الفشل: دور «موظف» بلا مطالبة EmployeeId صالحة لا يرى شيئاً —
        // هويةٌ ناقصة لا تُترجَم إلى سردٍ أوسع.
        if (ownEmployeeId == DeniedEmployeeId)
        {
            Requests = new List<RequestRow>();
            return;
        }

        var ownClause = ownEmployeeId.HasValue ? " AND r.EmployeeId = @OwnEmployeeId" : string.Empty;

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
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
WHERE {companyPredicate}{ownClause}
ORDER BY r.CreatedAt DESC;
""",
            command =>
            {
                if (ownEmployeeId.HasValue)
                {
                    HrmsDatabase.AddParameter(command, "@OwnEmployeeId", ownEmployeeId.Value);
                }
            },
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

    /// <summary>قيمةٌ حارسة تعني «دور موظف بهوية ناقصة» ⟹ لا يُسرد شيء.</summary>
    private static readonly int? DeniedEmployeeId = -1;

    /// <summary>
    /// معرّف الموظّف حين يكون الدور «موظف» — وإلا <c>null</c> (بلا قيدٍ إضافي فوق
    /// قيد الشركة). <see cref="DeniedEmployeeId"/> حين يكون الدور موظفاً بلا هوية.
    /// </summary>
    private int? ResolveOwnEmployeeIdForEmployeeRole()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) && id > 0
            ? id
            : DeniedEmployeeId;
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
