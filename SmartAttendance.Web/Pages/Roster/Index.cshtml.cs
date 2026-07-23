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

        Shifts = (await ShiftTypeStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();

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

        Cells = await RosterStore.GetCellsAsync(_dbContext, year, month);
        PublishedAt = await RosterStore.PublishedAtAsync(_dbContext, year, month);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var (year, month) = Period;
        var cells = ParseCells(year, month);
        await RosterStore.SaveCellsAsync(_dbContext, cells);
        TempData["SuccessMessage"] = $"تم حفظ الجدول ({cells.Count} خلية).";
        return RedirectToPage(new { Month, Search, PageNumber });
    }

    public async Task<IActionResult> OnPostPublishAsync()
    {
        var (year, month) = Period;
        var cells = ParseCells(year, month);
        await RosterStore.SaveCellsAsync(_dbContext, cells);
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
