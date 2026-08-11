using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Roster;

/// <summary>
/// جدولة مناوبات العمل / الروستر (/Roster) — الصفحة الثالثة بقسم «حضور الموظفين»
/// (نمط كيان): شبكة موظف×يوم لشهر، كل خلية مناوبة أو يوم عطلة/راحة، والأيام غير
/// المجدولة تسقط للمناوبة الافتراضية. حفظ + نشر الجدول. المحلل يقدّم الروستر.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 25;

    public record EmpRow(int Id, string No, string Name, string Department);

    public List<EmpRow> Employees { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public List<DateOnly> Days { get; set; } = new();
    public Dictionary<(int, DateOnly), RosterStore.Cell> Cells { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public DateTime? PublishedAt { get; set; }

    public (int Year, int Month) Period
    {
        get
        {
            if (DateTime.TryParse($"{Month}-01", out var p)) return (p.Year, p.Month);
            var t = DateTime.Today;
            return (t.Year, t.Month);
        }
    }

    public string DayName(DateOnly d) =>
        ShiftTypeStore.DayNames[DayAttendanceStore.ToDayIndex(d)];

    public async Task OnGetAsync()
    {
        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        // الفرشاة تعرض النشطة «المتاحة بالروستر» فقط — تعطيل AvailableInRoster يؤرشف
        // المناوبة من شاشة الجدولة بلا مساس بالتاريخ المالي (لا حذف أبداً).
        // الفرشاة منتقٍ معروض ⟹ محصورة بالنطاق (المشترك + شركاتي)، لا بالاحتساب.
        Shifts = (await ShiftTypeStore.ListInScopeAsync(_dbContext, await _companyScope.GetAsync()))
            .Where(s => s.IsActive && s.AvailableInRoster).ToList();

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        for (var d = from; d <= to; d = d.AddDays(1)) Days.Add(d);

        var query = _dbContext.Employees.AsNoTracking().Where(e => e.IsActive);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var v = Search.Trim();
            query = query.Where(e => e.EmployeeNo.Contains(v) || e.FullName.Contains(v));
        }
        TotalRows = await query.CountAsync();
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;

        Employees = await query.OrderBy(e => e.EmployeeNo)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize)
            .Select(e => new EmpRow(e.Id, e.EmployeeNo, e.FullName, e.Department.Name))
            .ToListAsync();

        Cells = await RosterStore.GetCellsAsync(_dbContext, await _companyScope.GetAsync(), year, month);
        PublishedAt = await RosterStore.PublishedAtAsync(_dbContext, year, month);
    }

    /// <summary>«ارسم أسبوعاً والباقي علينا»: تكرار الأسبوع الأول على بقية الشهر لكل الموظفين.</summary>
    public async Task<IActionResult> OnPostFillFromFirstWeekAsync()
    {
        var (year, month) = Period;
        var written = await RosterStore.FillMonthFromFirstWeekAsync(
            _dbContext, await _companyScope.GetAsync(), year, month);
        TempData["SuccessMessage"] = written > 0
            ? $"تمت تعبئة بقية الشهر من نمط الأسبوع الأول ({written} خلية) — راجع ثم انشر."
            : "لا خلايا بالأسبوع الأول لتكرارها — ارسم الأسبوع الأول أولاً (واحفظه) ثم أعد المحاولة.";
        return RedirectToPage(new { Month, Search, PageNumber });
    }

    /// <summary>نسخ جدول الشهر الماضي كاملاً بمحاذاة أيام الأسبوع (قبل 4 أسابيع يوماً بيوم).</summary>
    public async Task<IActionResult> OnPostCopyPrevMonthAsync()
    {
        var (year, month) = Period;
        var written = await RosterStore.CopyFromPreviousMonthAsync(
            _dbContext, await _companyScope.GetAsync(), year, month);
        TempData["SuccessMessage"] = written > 0
            ? $"تم نسخ جدول الشهر الماضي بمحاذاة أيام الأسبوع ({written} خلية) — عدّل الاستثناءات ثم انشر."
            : "الشهر الماضي بلا خلايا روستر — لا شيء يُنسخ.";
        return RedirectToPage(new { Month, Search, PageNumber });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var (year, month) = Period;
        var cells = ParseCells(year, month);
        var scope = await _companyScope.GetAsync();
        if (!await CellShiftsInScopeAsync(cells, scope)) return NotFound();

        await RosterStore.SaveCellsAsync(_dbContext, scope, cells);
        TempData["SuccessMessage"] = $"تم حفظ الجدول ({cells.Count} خلية).";
        return RedirectToPage(new { Month, Search, PageNumber });
    }

    /// <summary>
    /// كل معرّفات المناوبات بالخلايا داخل النطاق؟ الخلايا تُملَك بموظفها (يحرسه المتجر)،
    /// لكن **نوع المناوبة** مرجعٌ منفصل يأتي من قيمة الحقل — بلا هذا الحارس تُرسم
    /// مناوبة شركةٍ أخرى على جدول موظفيّ بطلبٍ معدَّل يدوياً.
    /// </summary>
    private Task<bool> CellShiftsInScopeAsync(
        List<RosterStore.Cell> cells, SmartAttendance.Web.Infrastructure.Security.CompanyScope scope) =>
        SmartAttendance.Web.Infrastructure.Security.ConfigTenantScope.AreAllInScopeAsync(
            _dbContext,
            SmartAttendance.Web.Infrastructure.Security.ConfigTenantScope.ShiftTypes,
            cells.Where(c => c.CellType == RosterStore.CellShift && c.ShiftTypeId is > 0)
                 .Select(c => c.ShiftTypeId!.Value),
            scope);

    public async Task<IActionResult> OnPostPublishAsync()
    {
        var (year, month) = Period;
        var cells = ParseCells(year, month);
        var scope = await _companyScope.GetAsync();
        if (!await CellShiftsInScopeAsync(cells, scope)) return NotFound();

        await RosterStore.SaveCellsAsync(_dbContext, scope, cells);
        await RosterStore.PublishAsync(_dbContext, year, month);
        TempData["SuccessMessage"] = $"تم نشر جدول {month:00}/{year}.";
        return RedirectToPage(new { Month, Search, PageNumber });
    }

    /// <summary>يقرأ خلايا النموذج cell_{empId}_{yyyy-MM-dd} = ""|S{shiftId}|Weekend|Rest.</summary>
    private List<RosterStore.Cell> ParseCells(int year, int month)
    {
        var result = new List<RosterStore.Cell>();
        foreach (var key in Request.Form.Keys)
        {
            if (!key.StartsWith("cell_")) continue;
            var parts = key.Split('_');            // ["cell", empId, yyyy-MM-dd]
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[1], out var empId)) continue;
            if (!DateOnly.TryParse(parts[2], out var date)) continue;

            var value = Request.Form[key].ToString();
            var cell = new RosterStore.Cell { EmployeeId = empId, WorkDate = date };
            if (string.IsNullOrWhiteSpace(value)) cell.CellType = "";   // ⇒ حذف
            else if (value == RosterStore.CellWeekend) cell.CellType = RosterStore.CellWeekend;
            else if (value == RosterStore.CellRest) cell.CellType = RosterStore.CellRest;
            else if (value.StartsWith("S") && int.TryParse(value[1..], out var sid))
            {
                cell.CellType = RosterStore.CellShift;
                cell.ShiftTypeId = sid;
            }
            else continue;
            result.Add(cell);
        }
        return result;
    }
}
