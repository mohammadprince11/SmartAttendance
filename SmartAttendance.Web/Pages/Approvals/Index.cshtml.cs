using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Approvals;

/// <summary>
/// شاشة الموافقات الخطوة-محورية: كل طلب يحمل لجنة مجمّدة من قالبه
/// (ApprovalWorkflowEngine) — الموافقة تقدّم الخطوة التالية والرفض نهائي.
/// الطلبات القديمة بلا سريان تُرحَّل كسولاً عند الفتح، والتصعيد يفحص كسولاً كذلك.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "Pending";

    [BindProperty]
    public string? Note { get; set; }

    public List<ApprovalRow> Requests { get; set; } = new();
    public Dictionary<int, ApprovalWorkflowEngine.FlowState> Flows { get; set; } = new();
    public Dictionary<int, List<DataChangeRequestStore.ProposedField>> DataChanges { get; set; } = new();

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        var escalated = await ApprovalWorkflowEngine.EscalateOverdueAsync(_dbContext);
        if (escalated > 0)
        {
            Message = $"تم تصعيد {escalated} طلب متأخر حسب قواعد قوالبها.";
        }
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        var result = await ApprovalWorkflowEngine.ApproveAsync(_dbContext, id, ActorName(), Note);
        Message = result.Message;
        MessageIsError = !result.Ok;

        // اعتماد نهائي لطلب تعديل بيانات → طبّق التعديلات على ملف الموظف.
        if (result.FinalApproved)
        {
            var applied = await DataChangeRequestStore.ApplyIfDataChangeAsync(
                _dbContext, id, ActorName(), HttpContext.Connection.RemoteIpAddress?.ToString());
            if (applied)
            {
                Message = "تم اعتماد الطلب وتطبيق التعديلات على بيانات الموظف.";
            }
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        var result = await ApprovalWorkflowEngine.RejectAsync(_dbContext, id, ActorName(), Note);
        Message = result.Message;
        MessageIsError = !result.Ok;
        await LoadAsync();
        return Page();
    }

    private string ActorName() => User?.Identity?.Name ?? "HR";

    private async Task LoadAsync()
    {
        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    r.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(m.FullName, '') AS DirectManager,
    r.RequestType,
    r.FromDate,
    r.ToDate,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.Reason, '') AS Reason,
    r.CreatedAt,
    r.EmployeeId
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE (@Status = 'All' OR r.Status = @Status)
ORDER BY r.CreatedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Status", Status),
            reader => new ApprovalRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        // فروقات طلبات تعديل البيانات (حقل: قديم → جديد) لعرضها للمُعتمِد.
        var dataChangeIds = Requests
            .Where(r => r.RequestType == DataChangeRequestStore.RequestTypeLabel)
            .Select(r => r.Id).ToList();
        DataChanges = await DataChangeRequestStore.ListFieldsForRequestsAsync(_dbContext, dataChangeIds);

        Flows = new Dictionary<int, ApprovalWorkflowEngine.FlowState>();
        foreach (var request in Requests)
        {
            var flow = await ApprovalWorkflowEngine.GetFlowAsync(_dbContext, request.Id);

            // ترحيل كسول: طلب معلّق قديم بلا سريان → نبدأ سريانه من قالب اليوم.
            if (flow == null && request.Status == "Pending")
            {
                await ApprovalWorkflowEngine.StartAsync(_dbContext, request.Id, request.RequestType, request.EmployeeId);
                flow = await ApprovalWorkflowEngine.GetFlowAsync(_dbContext, request.Id);
                if (flow?.Current != null)
                {
                    request.CurrentStep = flow.Current.DisplayName;
                }
            }

            if (flow != null)
            {
                Flows[request.Id] = flow;
            }
        }
    }

    public class ApprovalRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string DirectManager { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }
}
