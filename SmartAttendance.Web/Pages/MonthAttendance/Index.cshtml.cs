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

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "All"; // All | UnderReview | Approved | Locked

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

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

        var all = await MonthAttendanceStore.ListAsync(_dbContext, year, month);
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
        var count = await MonthAttendanceStore.BuildMonthAsync(_dbContext, year, month);
        TempData["SuccessMessage"] = count == 0
            ? "لا يوميات محللة لهذا الشهر — شغّل «تحديث الحضور» أولاً."
            : $"بُني شهر {month:00}/{year} — {count} موظفاً (أرقام المعتمد/المقفل لم تُمس).";
        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }

    public Task<IActionResult> OnPostApproveAsync() => TransitionAsync(
        MonthAttendanceStore.ApproveAsync, "اعتُمد {0} شهراً.");

    public Task<IActionResult> OnPostReopenAsync() => TransitionAsync(
        MonthAttendanceStore.ReopenAsync, "أُرجع {0} شهراً للمراجعة.");

    public Task<IActionResult> OnPostLockAsync() => TransitionAsync(
        MonthAttendanceStore.LockAsync, "قُفل {0} شهراً للرواتب.");

    private async Task<IActionResult> TransitionAsync(
        Func<ApplicationDbContext, IReadOnlyCollection<int>, Task<int>> action, string messageFormat)
    {
        var ids = Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد صفوفاً أولاً.";
        }
        else
        {
            var count = await action(_dbContext, ids);
            TempData["SuccessMessage"] = count == 0
                ? "لا صفوف بحالة تسمح بهذا الانتقال ضمن المحدد."
                : string.Format(messageFormat, count);
        }
        return RedirectToPage(new { Month, Search, Filter, PageNumber });
    }
}
