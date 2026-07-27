using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

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

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "Pending";
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
    public Dictionary<int, List<DataChangeRequestStore.ProposedField>> DataChanges { get; set; } = new();
    public Dictionary<int, FinancialRequestStore.Detail> FinancialRequests { get; set; } = new();

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
        // سجّل قرار الحقول قبل البتّ (طلبات تعديل البيانات فقط تتأثر؛ غيرها بلا حقول = بلا أثر).
        await DataChangeRequestStore.SetFieldDecisionsAsync(_dbContext, id, ApprovedFieldKeys);
        var result = await ApprovalWorkflowEngine.ApproveAsync(_dbContext, id, ActorName(), Note);
        Message = result.Message;
        MessageIsError = !result.Ok;
        if (result.FinalApproved) await ApplyEffectsAsync(id);
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

    /// <summary>اعتماد مجمّع: يقدّم كل طلب محدَّد خطوةً واحدة، ويُفعّل الأثر لِمَن اكتملت لجنته.</summary>
    public async Task<IActionResult> OnPostBulkApproveAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        int ok = 0, final = 0;
        foreach (var id in Ids.Distinct())
        {
            var r = await ApprovalWorkflowEngine.ApproveAsync(_dbContext, id, ActorName(), Note);
            if (r.Ok) ok++;
            if (r.FinalApproved) { await ApplyEffectsAsync(id); final++; }
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
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        int ok = 0;
        foreach (var id in Ids.Distinct())
        {
            var r = await ApprovalWorkflowEngine.RejectAsync(_dbContext, id, ActorName(), Note);
            if (r.Ok) ok++;
        }
        Message = ok == 0 ? "لم يُرفض أي طلب." : $"تم رفض {ok} طلب.";
        MessageIsError = ok == 0;
        await LoadAsync();
        return Page();
    }

    private async Task ApplyEffectsAsync(int id)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await DataChangeRequestStore.ApplyIfDataChangeAsync(_dbContext, id, ActorName(), ip);
        await FinancialRequestStore.ApplyIfFinancialAsync(_dbContext, id, ActorName(), ip);
        await ShiftRequestStore.ApplyIfShiftRequestAsync(_dbContext, id);
    }

    private string ActorName() => User?.Identity?.Name ?? "HR";

    private async Task LoadAsync()
    {
        await LoadFilterOptionsAsync();

        var counts = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT ISNULL(Status,'Pending') AS S, COUNT(*) AS C FROM SelfServiceRequests GROUP BY ISNULL(Status,'Pending');",
            null,
            reader => new { S = HrmsDatabase.GetString(reader, "S"), C = HrmsDatabase.GetInt(reader, "C") });
        PendingCount = counts.FirstOrDefault(x => x.S == "Pending")?.C ?? 0;
        ApprovedCount = counts.FirstOrDefault(x => x.S == "Approved")?.C ?? 0;
        RejectedCount = counts.FirstOrDefault(x => x.S == "Rejected")?.C ?? 0;

        Requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 300
    r.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(m.FullName, '') AS DirectManager,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(e.Position, '') AS Position,
    r.RequestType,
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
WHERE (@Status = 'All' OR r.Status = @Status)
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
    }

    private async Task LoadFilterOptionsAsync()
    {
        Departments = await HrmsDatabase.QueryAsync(_dbContext,
            "SELECT Id, Name FROM Departments ORDER BY Name",
            null, r => new Lookup(HrmsDatabase.GetInt(r, "Id"), HrmsDatabase.GetString(r, "Name")));
        Branches = await HrmsDatabase.QueryAsync(_dbContext,
            "SELECT Id, Name FROM Branches ORDER BY Name",
            null, r => new Lookup(HrmsDatabase.GetInt(r, "Id"), HrmsDatabase.GetString(r, "Name")));
        Positions = await HrmsDatabase.QueryAsync(_dbContext,
            "SELECT DISTINCT Position FROM Employees WHERE ISNULL(Position,'') <> '' ORDER BY Position",
            null, r => HrmsDatabase.GetString(r, "Position"));
        RequestTypes = await HrmsDatabase.QueryAsync(_dbContext,
            "SELECT DISTINCT RequestType FROM SelfServiceRequests WHERE ISNULL(RequestType,'') <> '' ORDER BY RequestType",
            null, r => HrmsDatabase.GetString(r, "RequestType"));
    }

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static object DateParam(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(RequestType) ||
        DepartmentId.HasValue || BranchId.HasValue || !string.IsNullOrWhiteSpace(Position) ||
        ReqFrom.HasValue || ReqTo.HasValue || ActFrom.HasValue || ActTo.HasValue;

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
