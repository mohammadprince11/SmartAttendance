using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.MonthAttendance;

/// <summary>
/// الحضور الشهري (/MonthAttendance) — المرحلة 6 من مودل الحضور بنمط كيان:
/// دورة حالة شهر الموظف (تحت المراجعة ← معتمد ← مقفل للرواتب) مع «بناء الشهر»
/// من اليوميات واعتماد/إرجاع/قفل جماعي. راجع قسمي 9 و13 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext dbContext,
        SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "All"; // All | UnderReview | Approved | Locked

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public string? UnlockReason { get; set; }

    public const int PageSize = 50;

    public List<MonthAttendanceStore.MonthRow> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public int UnderReviewCount { get; set; }
    public int ApprovedCount { get; set; }
    public int LockedCount { get; set; }

    public (int Year, int Month) Period
    {
        get
        {
            if (DateTime.TryParse($"{Month}-01", out var parsed)) return (parsed.Year, parsed.Month);
            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }

    public async Task OnGetAsync()
    {
        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        var all = await MonthAttendanceStore.ListAsync(_dbContext, await _companyScope.GetAsync(), year, month);
        UnderReviewCount = all.Count(r => r.Status == "UnderReview");
        ApprovedCount = all.Count(r => r.Status == "Approved");
        LockedCount = all.Count(r => r.Status == "Locked");

        var filtered = Filter == "All" ? all : all.Where(r => r.Status == Filter).ToList();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var value = Search.Trim();
            filtered = filtered.Where(r =>
                r.EmployeeNo.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        TotalRows = filtered.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnPostBuildAsync()
    {
        var (year, month) = Period;
        var count = await MonthAttendanceStore.BuildMonthAsync(_dbContext, await _companyScope.GetAsync(), year, month);

        // التجميع صار طازجاً ⟹ قيّم القواعد الفترية الشهرية عليه فوراً. الاقتراحات
        // تُحفظ معلّقة بشاشة «الإجراءات المقترحة» (لا تنفيذ تلقائي — راجع AnalyzePeriodAsync).
        var suggested = count == 0
            ? 0
            : await RecommendationStore.AnalyzePeriodAsync(_dbContext, await _companyScope.GetAsync(), "Month", year, month);

        TempData["SuccessMessage"] = count == 0
            ? "لا يوميات محللة لهذا الشهر — شغّل «تحديث الحضور» أولاً."
            : $"بُني شهر {month:00}/{year} — {count} موظفاً (أرقام المعتمد/المقفل لم تُمس)."
              + (suggested > 0 ? $" وأنتجت القواعد الفترية {suggested} اقتراحاً جديداً." : string.Empty);
        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }

    /// <summary>الاعتماد ببوابة التحليل: الشهر ناقص اليوميات لا يُعتمد فلا يصل المسير.</summary>
    public async Task<IActionResult> OnPostApproveAsync()
    {
        var ids = SelectedIds();

        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد صفوفاً أولاً.";
            return RedirectToPage(new { Month, Search, Filter, PageNumber });
        }

        var (approved, blocked) = await MonthAttendanceStore.ApproveWithGateAsync(
            _dbContext, await _companyScope.GetAsync(HttpContext.RequestAborted), ids);

        TempData["SuccessMessage"] = (approved, blocked) switch
        {
            (0, > 0) => $"لم يُعتمد شيء — {blocked} صفاً بأيام غير محلّلة. شغّل «تحديث الحضور» للشهر أولاً.",
            (> 0, > 0) => $"اعتُمد {approved} شهراً، وحُجب {blocked} لأيام غير محلّلة.",
            (> 0, 0) => $"اعتُمد {approved} شهراً.",
            _ => "لا صفوف بحالة تسمح بهذا الانتقال ضمن المحدد."
        };

        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }

    public Task<IActionResult> OnPostReopenAsync() => TransitionAsync(
        MonthAttendanceStore.ReopenAsync, "أُرجع {0} شهراً للمراجعة.");

    public Task<IActionResult> OnPostLockAsync() => TransitionAsync(
        MonthAttendanceStore.LockAsync, "قُفل {0} شهراً للرواتب.");

    public async Task<IActionResult> OnPostUnlockAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(UnlockReason))
            TempData["SuccessMessage"] = "حدد صفوفاً مقفلة واكتب سبب الفتح.";
        else
        {
            var count = await MonthAttendanceStore.UnlockAsync(
                _dbContext, await _companyScope.GetAsync(HttpContext.RequestAborted), ids,
                User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), UnlockReason);
            TempData["SuccessMessage"] = count == 0
                ? "لا صفوف مقفلة ضمن المحدد أو لا تملك نطاقها."
                : $"فُتح {count} شهراً إلى حالة معتمد وسُجل السبب في سجل التدقيق.";
        }
        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }

    private List<int> SelectedIds() =>
        Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private async Task<IActionResult> TransitionAsync(
        Func<ApplicationDbContext, SmartAttendance.Web.Infrastructure.Security.CompanyScope, IReadOnlyCollection<int>, Task<int>> action,
        string messageFormat)
    {
        var ids = SelectedIds();

        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد صفوفاً أولاً.";
        }
        else
        {
            var count = await action(_dbContext, await _companyScope.GetAsync(HttpContext.RequestAborted), ids);
            TempData["SuccessMessage"] = count == 0
                ? "لا صفوف بحالة تسمح بهذا الانتقال ضمن المحدد."
                : string.Format(messageFormat, count);
        }
        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }
}
