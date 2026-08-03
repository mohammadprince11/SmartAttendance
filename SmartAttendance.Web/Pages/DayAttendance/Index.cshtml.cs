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

    /// <summary>
    /// فلتر الحالة من أزرار العدّادات أعلى الشاشة: Present · Late · Incomplete ·
    /// Absent. فارغ = الكل. العدّادات تبقى محسوبة على **كل** الشهر لا على
    /// المفلتَر، وإلا صار الضغط على «متأخر» يصفّر بقية الأزرار فيستحيل الرجوع.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

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

        // الفلتر يُطبَّق بعد العدّادات، والترتيب من الأحدث للأقدم ثم برقم الموظف
        // ليكون ترتيب الصفحات ثابتاً (بلا مُرتِّب ثانوي تتبدّل الصفوف بين الصفحات).
        var view = string.IsNullOrWhiteSpace(StatusFilter)
            ? all
            : all.Where(row => row.Status == StatusFilter).ToList();

        view = view
            .OrderByDescending(row => row.WorkDate)
            .ThenBy(row => row.EmployeeNo, StringComparer.Ordinal)
            .ToList();

        TotalRows = view.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = view.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        var (year, month) = Period;
        var shiftTypeId = int.TryParse(Request.Form["ShiftTypeId"], out var id) ? id : 0;

        // الفحص المسبق يقول **لماذا** لم يُحلَّل شيء. بدونه كان المحرّك يعيد صفراً
        // صامتاً فتُقرأ الرسالة «تم التحديث — 0 يومية» كأنها نجاح، والشاشة فارغة.
        var blocker = await DayAttendanceStore.FindAnalyzeBlockerAsync(
            _dbContext, year, month, shiftTypeId);

        if (blocker is not null)
        {
            TempData["SuccessMessage"] = blocker;
        }
        else
        {
            var count = await DayAttendanceStore.AnalyzeMonthAsync(_dbContext, year, month, shiftTypeId);

            TempData["SuccessMessage"] = count > 0
                ? $"تم تحديث الحضور — {count} يومية مولّدة لشهر {month:00}/{year}."
                : $"لم تُولَّد يوميات لشهر {month:00}/{year} — لا بصمات بالفترة " +
                  "للموظفين المسنَدين لهذه المناوبة. تحقّق من إسناد الموظفين ومن نطاق تواريخ البصمات.";
        }
        return RedirectToPage(new { Month, Search, StatusFilter });
    }
}
