using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.DayAttendance;

/// <summary>
/// الحضور اليومي (/DayAttendance) — المرحلة 3 من مودل الحضور بنمط كيان:
/// يوميات موظف×يوم بالحقول المشتقة (تأخير/خروج مبكر/ساعات/حالة/تم التحليل)،
/// وزر «تحديث الحضور» يعيد بناء الشهر من البصمات الخام مقابل مناوبة مختارة.
/// راجع قسمي 9 و15 بدراسة الحضور.
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
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 50;

    public List<DayAttendanceStore.DayRow> Rows { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public int PresentCount { get; set; }
    public int LateCount { get; set; }
    public int AbsentCount { get; set; }
    public int IncompleteCount { get; set; }

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

        Shifts = (await ShiftTypeStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();

        var all = await DayAttendanceStore.ListAsync(_dbContext, year, month, Search);
        PresentCount = all.Count(r => r.Status == "Present");
        LateCount = all.Count(r => r.Status == "Late");
        AbsentCount = all.Count(r => r.Status == "Absent");
        IncompleteCount = all.Count(r => r.Status == "Incomplete");

        TotalRows = all.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = all.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        var (year, month) = Period;
        var shiftTypeId = int.TryParse(Request.Form["ShiftTypeId"], out var id) ? id : 0;

        if (shiftTypeId <= 0)
        {
            TempData["SuccessMessage"] = "اختر مناوبة التحليل أولاً.";
        }
        else
        {
            var count = await DayAttendanceStore.AnalyzeMonthAsync(_dbContext, year, month, shiftTypeId);
            TempData["SuccessMessage"] = $"تم تحديث الحضور — {count} يومية مولّدة لشهر {month:00}/{year}.";
        }
        return RedirectToPage(new { Month, Search });
    }
}
