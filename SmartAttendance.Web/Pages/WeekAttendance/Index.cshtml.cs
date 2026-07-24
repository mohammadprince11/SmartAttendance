using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.WeekAttendance;

/// <summary>
/// الحضور الأسبوعي (/WeekAttendance) — نظير الحضور الشهري بتجميع أسبوعي (أسابيع ISO)
/// بدورة UnderReview←Approved←Locked و«بناء الأسبوع» من اليوميات. راجع قسم 36.د بالدراسة.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)] public int? IsoYear { get; set; }
    [BindProperty(SupportsGet = true)] public int? Week { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "All";
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public const int PageSize = 50;

    public List<WeekAttendanceStore.WeekRow> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public int UnderReviewCount { get; set; }
    public int ApprovedCount { get; set; }
    public int LockedCount { get; set; }
    public int WeeksInYear { get; set; }
    public (DateOnly Start, DateOnly End) Range { get; set; }

    public (int Year, int Week) Period
    {
        get
        {
            var (curYear, curWeek) = WeekAttendanceStore.Current();
            var year = IsoYear is > 1999 and < 2100 ? IsoYear.Value : curYear;
            var maxWeek = WeekAttendanceStore.WeeksInYear(year);
            var week = Week is int w && w >= 1 && w <= maxWeek ? w : (year == curYear ? curWeek : 1);
            return (year, week);
        }
    }

    public async Task OnGetAsync()
    {
        var (year, week) = Period;
        IsoYear = year;
        Week = week;
        WeeksInYear = WeekAttendanceStore.WeeksInYear(year);
        Range = WeekAttendanceStore.WeekRange(year, week);

        var all = await WeekAttendanceStore.ListAsync(_dbContext, year, week);
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

    private object Route() => new { IsoYear, Week, Search, Filter, PageNumber };

    public async Task<IActionResult> OnPostBuildAsync()
    {
        var (year, week) = Period;
        var count = await WeekAttendanceStore.BuildWeekAsync(_dbContext, year, week);
        TempData["SuccessMessage"] = count == 0
            ? "لا يوميات محللة لهذا الأسبوع — شغّل «تحديث الحضور» أولاً."
            : $"بُني الأسبوع {week} لسنة {year} — {count} موظفاً (أرقام المعتمد/المقفل لم تُمس).";
        return RedirectToPage(Route());
    }

    public Task<IActionResult> OnPostApproveAsync() => TransitionAsync(
        WeekAttendanceStore.ApproveAsync, "اعتُمد {0} أسبوعاً.");

    public Task<IActionResult> OnPostReopenAsync() => TransitionAsync(
        WeekAttendanceStore.ReopenAsync, "أُرجع {0} أسبوعاً للمراجعة.");

    public Task<IActionResult> OnPostLockAsync() => TransitionAsync(
        WeekAttendanceStore.LockAsync, "قُفل {0} أسبوعاً.");

    private List<int> SelectedIds() =>
        Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private async Task<IActionResult> TransitionAsync(
        Func<ApplicationDbContext, IReadOnlyCollection<int>, Task<int>> action, string messageFormat)
    {
        var ids = SelectedIds();
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
        return RedirectToPage(Route());
    }
}
