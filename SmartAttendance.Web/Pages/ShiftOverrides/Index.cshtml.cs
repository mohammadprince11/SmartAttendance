using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.ShiftOverrides;

/// <summary>
/// تعديل مناوبات مؤقت (/ShiftOverrides) — الصفحة الثانية بقسم «حضور الموظفين» (نمط
/// كيان): قائمة سجلات التجاوز (موظف ← مناوبة بديلة لفترة) + إنشاء لنطاق (الكل/محدد).
/// المحلل يعطي التجاوز الأولوية على التعيين الدائم لأيام الفترة.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public record EmpLookup(int Id, string No, string Name);

    public List<ShiftOverrideStore.OverrideRow> Rows { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public List<EmpLookup> Employees { get; set; } = new();
    public int TotalRows { get; set; }

    public async Task OnGetAsync()
    {
        Shifts = (await ShiftTypeStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();
        Employees = await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.EmployeeNo)
            .Select(e => new EmpLookup(e.Id, e.EmployeeNo, e.FullName)).ToListAsync();

        var all = await ShiftOverrideStore.ListAsync(_dbContext);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var v = Search.Trim();
            all = all.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.NewShiftName.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        TotalRows = all.Count;
        Rows = all;
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var form = Request.Form;
        var shiftTypeId = int.TryParse(form["ShiftTypeId"], out var sid) ? sid : 0;
        var scope = form["Scope"].ToString() is { Length: > 0 } sc ? sc : "Selected";
        DateOnly.TryParse(form["FromDate"], out var from);
        DateOnly.TryParse(form["ToDate"], out var to);

        if (shiftTypeId <= 0 || from == default || to == default)
        {
            TempData["SuccessMessage"] = "اختر المناوبة وفترة التعديل.";
            return RedirectToPage(new { Search });
        }

        List<int> employeeIds;
        if (scope == "All")
        {
            employeeIds = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.IsActive).Select(e => e.Id).ToListAsync();
        }
        else
        {
            employeeIds = form["EmployeeIds"]
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(id => id > 0).Distinct().ToList();
        }

        if (employeeIds.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد موظفاً واحداً على الأقل (أو اختر «الكل»).";
            return RedirectToPage(new { Search });
        }

        var count = await ShiftOverrideStore.CreateAsync(_dbContext, employeeIds, shiftTypeId, from, to);
        TempData["SuccessMessage"] = $"تم إنشاء تعديل مؤقت لـ{count} موظفاً ({from:yyyy-MM-dd} → {to:yyyy-MM-dd}).";
        return RedirectToPage(new { Search });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var ids = Request.Form["SelectedIds"]
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();

        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد تعديلات لحذفها.";
        }
        else
        {
            await ShiftOverrideStore.DeleteAsync(_dbContext, ids);
            TempData["SuccessMessage"] = $"حُذف {ids.Count} تعديل مؤقت.";
        }
        return RedirectToPage(new { Search });
    }
}
