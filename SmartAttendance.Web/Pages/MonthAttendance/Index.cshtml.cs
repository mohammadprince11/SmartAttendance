using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
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
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<CompanyOption> Companies { get; set; } = new();

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
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        await LoadCompaniesAsync(scope);

        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        var viewScope = SelectedCompanyScope(scope);
        var all = await MonthAttendanceStore.ListAsync(_dbContext, viewScope, year, month);
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
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value))
        {
            TempData["SuccessMessage"] = "اختر شركة واحدة قبل بناء الحضور الشهري حتى تُطبَّق دورة الحضور وسياساتها الصحيحة.";
            return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
        }

        var selectedScope = CompanyScope.ForCompanies(new[] { CompanyId.Value });
        var (year, month) = Period;
        var count = await MonthAttendanceStore.BuildMonthAsync(
            _dbContext, selectedScope, year, month, CompanyId.Value);

        // التجميع صار طازجاً ⟹ قيّم القواعد الفترية الشهرية عليه فوراً للشركة نفسها.
        var suggested = count == 0
            ? 0
            : await RecommendationStore.AnalyzePeriodAsync(
                _dbContext, selectedScope, "Month", year, month);

        TempData["SuccessMessage"] = count == 0
            ? "لا يوميات محللة لهذا الشهر — شغّل «تحديث الحضور» أولاً."
            : $"بُني شهر {month:00}/{year} — {count} موظفاً (أرقام المعتمد/المقفل لم تُمس)."
              + (suggested > 0 ? $" وأنتجت القواعد الفترية {suggested} اقتراحاً جديداً." : string.Empty);
        return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
    }

    /// <summary>الاعتماد ببوابة التحليل: الشهر ناقص اليوميات لا يُعتمد فلا يصل المسير.</summary>
    public async Task<IActionResult> OnPostApproveAsync()
    {
        var ids = SelectedIds();

        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد صفوفاً أولاً.";
            return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
        }

        var approvalScope = SelectedCompanyScope(
            await _companyScope.GetAsync(HttpContext.RequestAborted));
        var (approved, blocked) = await MonthAttendanceStore.ApproveWithGateAsync(
            _dbContext, approvalScope, ids);

        TempData["SuccessMessage"] = (approved, blocked) switch
        {
            (0, > 0) => $"لم يُعتمد شيء — {blocked} صفاً بأيام غير محلّلة. شغّل «تحديث الحضور» للشهر أولاً.",
            (> 0, > 0) => $"اعتُمد {approved} شهراً، وحُجب {blocked} لأيام غير محلّلة.",
            (> 0, 0) => $"اعتُمد {approved} شهراً.",
            _ => "لا صفوف بحالة تسمح بهذا الانتقال ضمن المحدد."
        };

        return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
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
            var unlockScope = SelectedCompanyScope(
                await _companyScope.GetAsync(HttpContext.RequestAborted));
            var count = await MonthAttendanceStore.UnlockAsync(
                _dbContext, unlockScope, ids,
                User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), UnlockReason);
            TempData["SuccessMessage"] = count == 0
                ? "لا صفوف مقفلة ضمن المحدد أو لا تملك نطاقها."
                : $"فُتح {count} شهراً إلى حالة معتمد وسُجل السبب في سجل التدقيق.";
        }
        return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
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
            var transitionScope = SelectedCompanyScope(
                await _companyScope.GetAsync(HttpContext.RequestAborted));
            var count = await action(_dbContext, transitionScope, ids);
            TempData["SuccessMessage"] = count == 0
                ? "لا صفوف بحالة تسمح بهذا الانتقال ضمن المحدد."
                : string.Format(messageFormat, count);
        }
        return RedirectToPage(new { CompanyId, Month, Search, Filter, PageNumber });
    }

    private async Task LoadCompaniesAsync(CompanyScope scope)
    {
        var query = _dbContext.Companies.AsNoTracking()
            .Where(company => !company.IsDeleted && company.IsActive);

        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }

        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name))
            .ToListAsync();

        if (CompanyId is > 0 && !scope.Allows(CompanyId.Value))
            CompanyId = null;

        if (CompanyId is null && Companies.Count == 1)
            CompanyId = Companies[0].Id;
    }

    private CompanyScope SelectedCompanyScope(CompanyScope baseScope) =>
        CompanyId is > 0 && baseScope.Allows(CompanyId.Value)
            ? CompanyScope.ForCompanies(new[] { CompanyId.Value })
            : baseScope;

    public sealed record CompanyOption(int Id, string Name);
}
