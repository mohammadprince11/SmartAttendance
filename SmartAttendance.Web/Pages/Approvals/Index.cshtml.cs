using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Approvals;

/// <summary>
/// شاشة الموافقات الخطوة-محورية: كل طلب يحمل لجنة مجمّدة من قالبه
/// (ApprovalWorkflowEngine) — الموافقة تقدّم الخطوة التالية والرفض نهائي.
/// تدعم فلاتر متعددة (بحث/نوع/قسم/فرع/منصب/تواريخ) واعتماداً/رفضاً مجمّعاً للطلبات المحددة.
/// الطلبات القديمة بلا سريان تُرحَّل كسولاً عند الفتح، والتصعيد يفحص كسولاً كذلك.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    private Task<CompanyScope> ScopeAsync() => _companyScope.GetAsync(HttpContext.RequestAborted);

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "Pending";
    [BindProperty(SupportsGet = true)] public string Source { get; set; } = "All";
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? RequestType { get; set; }
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Position { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ReqFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ReqTo { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ActFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ActTo { get; set; }

    [BindProperty] public string? Note { get; set; }
    [BindProperty] public List<int> Ids { get; set; } = new();
    // الحقول المُعتمدة ضمن طلب تعديل بيانات (البقية تُرفض) — قرار على مستوى الحقل.
    [BindProperty] public List<string> ApprovedFieldKeys { get; set; } = new();

    public List<ApprovalRow> Requests { get; set; } = new();
    public Dictionary<int, ApprovalWorkflowEngine.FlowState> Flows { get; set; } = new();
    public Dictionary<int, List<ApprovalWorkflowEngine.HistoryState>> Histories { get; set; } = new();
    public Dictionary<int, List<DataChangeRequestStore.ProposedField>> DataChanges { get; set; } = new();
    public Dictionary<int, FinancialRequestStore.Detail> FinancialRequests { get; set; } = new();
    public Dictionary<int, List<FormSubmissionStore.Answer>> CustomRequestAnswers { get; set; } = new();

    // قوائم الفلاتر
    public List<Lookup> Departments { get; set; } = new();
    public List<Lookup> Branches { get; set; } = new();
    public List<string> Positions { get; set; } = new();
    public List<string> RequestTypes { get; set; } = new();

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    // ملخّص علوي (مستقل عن الفلتر): أعداد الطلبات بكل حالة.
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }

    public async Task OnGetAsync()
    {
        var sla = await ApprovalWorkflowEngine.ProcessSlaAsync(_dbContext);
        if (sla.Escalated > 0||sla.Reminded>0)
        {
            Message = $"معالجة SLA: {sla.Reminded} تذكير، {sla.Escalated} تصعيد.";
        }
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var result = await ApprovalWorkflowEngine.ApproveAsync(
            _dbContext, await ScopeAsync(), id, ActorName(), Note, ActorRoles(), ActorEmployeeId());
        // لا نكتب قرارات الحقول إلا بعد قبول المحرك لهوية صاحب الخطوة؛ وإلا أمكن
        // لمستخدم يرى الشاشة أن يغيّر قرارات طلب ثم يترك اعتماده لشخص مخوّل.
        if (result.Ok)
            await DataChangeRequestStore.SetFieldDecisionsAsync(_dbContext, id, ApprovedFieldKeys);
        Message = result.Message;
        MessageIsError = !result.Ok;
        if (result.FinalApproved) await ApplyEffectsAsync(id, await ScopeAsync());
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        var result = await ApprovalWorkflowEngine.RejectAsync(
            _dbContext, await ScopeAsync(), id, ActorName(), Note, ActorRoles(), ActorEmployeeId());
        Message = result.Message;
        MessageIsError = !result.Ok;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReturnAsync(int id)
    {
        var result = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            _dbContext, await ScopeAsync(), id, ActorName(), Note, ActorRoles(), ActorEmployeeId());
        Message=result.Message; MessageIsError=!result.Ok;
        await LoadAsync();
        return Page();
    }

    /// <summary>اعتماد مجمّع: يقدّم كل طلب محدَّد خطوةً واحدة، ويُفعّل الأثر لِمَن اكتملت لجنته.</summary>
    public async Task<IActionResult> OnPostBulkApproveAsync()
    {
        int ok = 0, final = 0;
        var scope = await ScopeAsync();
        foreach (var id in Ids.Distinct())
        {
            var r = await ApprovalWorkflowEngine.ApproveAsync(
                _dbContext, scope, id, ActorName(), Note, ActorRoles(), ActorEmployeeId());
            if (r.Ok) ok++;
            if (r.FinalApproved) { await ApplyEffectsAsync(id, scope); final++; }
        }
        Message = ok == 0 ? "لم يُعتمد أي طلب (تحقق من الصلاحية/الخطوة)." :
            $"تم اعتماد خطوة لـ {ok} طلب" + (final > 0 ? $"، منها {final} اكتملت لجنتها وفُعِّل أثرها." : ".");
        MessageIsError = ok == 0;
        await LoadAsync();
        return Page();
    }

    /// <summary>رفض مجمّع للطلبات المحددة.</summary>
    public async Task<IActionResult> OnPostBulkRejectAsync()
    {
        int ok = 0;
        var scope = await ScopeAsync();
        foreach (var id in Ids.Distinct())
        {
            var r = await ApprovalWorkflowEngine.RejectAsync(
                _dbContext, scope, id, ActorName(), Note, ActorRoles(), ActorEmployeeId());
            if (r.Ok) ok++;
        }
        Message = ok == 0 ? "لم يُرفض أي طلب." : $"تم رفض {ok} طلب.";
        MessageIsError = ok == 0;
        await LoadAsync();
        return Page();
    }

    private async Task ApplyEffectsAsync(int id, CompanyScope scope)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await DataChangeRequestStore.ApplyIfDataChangeAsync(_dbContext, id, ActorName(), ip);
        await FinancialRequestStore.ApplyIfFinancialAsync(_dbContext, scope, id, ActorName(), ip);
        await ShiftRequestStore.ApplyIfShiftRequestAsync(_dbContext, id);

        // الإجازة/المغادرة المعتمَدة تغيّر اليومية — أعد تحليلها إن كان المفتاح مفعّلاً.
        await AttendanceReanalysisPolicy.ApplyIfAttendanceAffectingAsync(_dbContext, id);
    }

    private string ActorName() => User?.Identity?.Name ?? "HR";

    private IEnumerable<string> ActorRoles() =>
        User.Claims.Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(claim => claim.Value);

    private int? ActorEmployeeId() =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) && id > 0 ? id : null;

    private async Task LoadAsync()
    {
        // 🔴 كانت هذه الشاشة تسرد وتعدّ طلبات **كل الشركات**: مستخدم مقيَّد يرى طلبات
        // موظفي شركاتٍ أخرى ويبتّها. الحصر بوصل الطلب بموظفه ثم بنطاق المستخدم.
        var scope = await ScopeAsync();
        Source = Source is "SelfService" or "Admin" or "Legacy" ? Source : "All";
        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        await LoadFilterOptionsAsync(scope);

        var counts = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT ISNULL(r.Status,'Pending') AS S, COUNT(*) AS C
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id = r.EmployeeId
WHERE {scopeFilter}
  AND (@Source = 'All' OR r.RequestSource = @Source)
GROUP BY ISNULL(r.Status,'Pending');
""",
            command => HrmsDatabase.AddParameter(command, "@Source", Source),
            reader => new { S = HrmsDatabase.GetString(reader, "S"), C = HrmsDatabase.GetInt(reader, "C") });
        PendingCount = counts.FirstOrDefault(x => x.S == "Pending")?.C ?? 0;
        ApprovedCount = counts.FirstOrDefault(x => x.S == "Approved")?.C ?? 0;
        RejectedCount = counts.FirstOrDefault(x => x.S == "Rejected")?.C ?? 0;

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT TOP 300
    r.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(m.FullName, '') AS DirectManager,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(e.Position, '') AS Position,
    r.RequestType,
    r.RequestSource,
    r.FromDate,
    r.ToDate,
    r.StartTime,
    r.EndTime,
    r.DaysCount,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.Reason, '') AS Reason,
    r.CreatedAt,
    r.UpdatedAt,
    r.EmployeeId
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE {scopeFilter}
  AND (@Source = 'All' OR r.RequestSource = @Source)
  AND (@Status = 'All' OR r.Status = @Status)
  AND (@Search IS NULL OR e.FullName LIKE '%' + @Search + '%' OR e.EmployeeNo LIKE '%' + @Search + '%')
  AND (@ReqType IS NULL OR r.RequestType = @ReqType)
  AND (@DeptId IS NULL OR e.DepartmentId = @DeptId)
  AND (@BranchId IS NULL OR e.BranchId = @BranchId)
  AND (@Position IS NULL OR e.Position = @Position)
  AND (@ReqFrom IS NULL OR CAST(r.CreatedAt AS date) >= @ReqFrom)
  AND (@ReqTo   IS NULL OR CAST(r.CreatedAt AS date) <= @ReqTo)
  AND (@ActFrom IS NULL OR CAST(r.UpdatedAt AS date) >= @ActFrom)
  AND (@ActTo   IS NULL OR CAST(r.UpdatedAt AS date) <= @ActTo)
ORDER BY r.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", Status);
                HrmsDatabase.AddParameter(command, "@Source", Source);
                HrmsDatabase.AddParameter(command, "@Search", NullIfEmpty(Search));
                HrmsDatabase.AddParameter(command, "@ReqType", NullIfEmpty(RequestType));
                HrmsDatabase.AddParameter(command, "@DeptId", (object?)DepartmentId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@BranchId", (object?)BranchId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Position", NullIfEmpty(Position));
                HrmsDatabase.AddParameter(command, "@ReqFrom", DateParam(ReqFrom));
                HrmsDatabase.AddParameter(command, "@ReqTo", DateParam(ReqTo));
                HrmsDatabase.AddParameter(command, "@ActFrom", DateParam(ActFrom));
                HrmsDatabase.AddParameter(command, "@ActTo", DateParam(ActTo));
            },
            reader => new ApprovalRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager"),
                Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                Branch = HrmsDatabase.GetString(reader, "BranchName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestSource = HrmsDatabase.GetString(reader, "RequestSource"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                StartTime = HrmsDatabase.GetTimeSpan(reader, "StartTime"),
                EndTime = HrmsDatabase.GetTimeSpan(reader, "EndTime"),
                DaysCount = HrmsDatabase.GetNullableDecimal(reader, "DaysCount"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                UpdatedAt = HrmsDatabase.GetDateTime(reader, "UpdatedAt")
            });

        var dataChangeIds = Requests
            .Where(r => r.RequestType == DataChangeRequestStore.RequestTypeLabel)
            .Select(r => r.Id).ToList();
        DataChanges = await DataChangeRequestStore.ListFieldsForRequestsAsync(_dbContext, dataChangeIds);

        FinancialRequests = await FinancialRequestStore.ListForRequestsAsync(_dbContext, Requests.Select(r => r.Id));
        CustomRequestAnswers = await FormSubmissionStore.LoadAnswersForRequestsAsync(
            _dbContext, Requests.Select(r => r.Id), scope);

        Flows = new Dictionary<int, ApprovalWorkflowEngine.FlowState>();
        foreach (var request in Requests)
        {
            var flow = await ApprovalWorkflowEngine.GetFlowAsync(_dbContext, request.Id);
            if (flow == null && request.Status == "Pending")
            {
                await ApprovalWorkflowEngine.StartAsync(_dbContext, request.Id, request.RequestType, request.EmployeeId);
                flow = await ApprovalWorkflowEngine.GetFlowAsync(_dbContext, request.Id);
                if (flow?.Current != null) request.CurrentStep = flow.Current.DisplayName;
            }
            if (flow != null) Flows[request.Id] = flow;
        }
        Histories = await ApprovalWorkflowEngine.GetHistoriesAsync(_dbContext, scope, Requests.Select(request => request.Id));
    }

    private async Task LoadFilterOptionsAsync(CompanyScope scope)
    {
        var companyPredicate = scope.ToSqlPredicate("CompanyId");
        Departments = await HrmsDatabase.QueryAsync(_dbContext,
            $"SELECT Id,Name FROM Departments WHERE IsDeleted=0 AND {companyPredicate} ORDER BY Name",
            null, r => new Lookup(HrmsDatabase.GetInt(r, "Id"), HrmsDatabase.GetString(r, "Name")));
        Branches = await HrmsDatabase.QueryAsync(_dbContext,
            $"SELECT Id,Name FROM Branches WHERE IsDeleted=0 AND {companyPredicate} ORDER BY Name",
            null, r => new Lookup(HrmsDatabase.GetInt(r, "Id"), HrmsDatabase.GetString(r, "Name")));
        Positions = await HrmsDatabase.QueryAsync(_dbContext,
            $"SELECT DISTINCT Position FROM Employees WHERE IsDeleted=0 AND {companyPredicate} AND ISNULL(Position,'')<>'' ORDER BY Position",
            null, r => HrmsDatabase.GetString(r, "Position"));
        RequestTypes = await HrmsDatabase.QueryAsync(_dbContext,
            $"SELECT DISTINCT r.RequestType FROM SelfServiceRequests r INNER JOIN Employees e ON e.Id=r.EmployeeId WHERE e.IsDeleted=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")} AND ISNULL(r.RequestType,'')<>'' ORDER BY r.RequestType",
            null, r => HrmsDatabase.GetString(r, "RequestType"));
    }

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static object DateParam(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(RequestType) ||
        DepartmentId.HasValue || BranchId.HasValue || !string.IsNullOrWhiteSpace(Position) ||
        ReqFrom.HasValue || ReqTo.HasValue || ActFrom.HasValue || ActTo.HasValue || Source != "All";

    public record Lookup(int Id, string Name);

    public class ApprovalRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string DirectManager { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string RequestSource { get; set; } = string.Empty;
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public decimal? DaysCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
