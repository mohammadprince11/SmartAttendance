using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceViewer;

/// <summary>
/// مستعرض الحضور (/AttendanceViewer) — المرحلة 5 من مودل الحضور بنمط كيان:
/// مصفوفة موظف × أيام الشهر بمفتاح حالات ملون + عمود المجموع (حاضر/أيام عمل).
/// يقرأ من يوميات DayAttendances المولّدة بـ«تحديث الحضور».
/// راجع قسمي 3.4 و13 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 20;

    public sealed class EmployeeRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public Dictionary<int, DayAttendanceStore.DayRow> Days { get; set; } = new(); // اليوم ← اليومية
        public int PresentDays { get; set; }
        public int WorkDays { get; set; }
    }

    public List<EmployeeRow> Rows { get; set; } = new();
    public int DaysInMonth { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }

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
        DaysInMonth = DateTime.DaysInMonth(year, month);

        var all = await DayAttendanceStore.ListAsync(_dbContext, year, month, Search);

        var employees = all
            .GroupBy(r => (r.EmployeeId, r.EmployeeNo, r.EmployeeName))
            .Select(g => new EmployeeRow
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeNo = g.Key.EmployeeNo,
                EmployeeName = g.Key.EmployeeName,
                Days = g.ToDictionary(r => r.WorkDate.Day),
                PresentDays = g.Count(r => r.Status is "Present" or "Late"),
                WorkDays = g.Count(r => r.DayKind == "Work")
            })
            .OrderBy(e => e.EmployeeNo)
            .ToList();

        TotalRows = employees.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = employees.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }
}
