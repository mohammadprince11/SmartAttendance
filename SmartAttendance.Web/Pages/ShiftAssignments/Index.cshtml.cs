using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.ShiftAssignments;

/// <summary>
/// مناوبات الموظفين الثابتة (/ShiftAssignments) — المرحلة 5 من مودل الحضور بنمط
/// كيان («تحديد مناوبات الموظفين» الجماعي): بحث + تحديد متعدد + تعيين/إلغاء
/// مناوبة ShiftType. المحلل يستخدم هذا التعيين. راجع قسمي 13 و15 بدراسة الحضور.
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

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "All";   // All | Assigned | Unassigned

    public List<EmployeeShiftTypeStore.AssignmentRow> Rows { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public int TotalRows { get; set; }
    public int AssignedCount { get; set; }
    public int UnassignedCount { get; set; }

    public async Task OnGetAsync()
    {
        Shifts = (await ShiftTypeStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();

        var all = await EmployeeShiftTypeStore.ListAsync(_dbContext);
        AssignedCount = all.Count(r => r.ShiftTypeId != null);
        UnassignedCount = all.Count - AssignedCount;

        var filtered = all;
        if (Filter == "Assigned") filtered = filtered.Where(r => r.ShiftTypeId != null).ToList();
        if (Filter == "Unassigned") filtered = filtered.Where(r => r.ShiftTypeId == null).ToList();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var value = Search.Trim();
            filtered = filtered.Where(r =>
                r.EmployeeNo.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.Position.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.Branch.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        TotalRows = filtered.Count;
        Rows = filtered;

        // المناوبة الفعّالة لكل صف: تعيين يدوي ← مطابقة معايير استحقاق ← الافتراضية
        // (نفس ترتيب DayAttendanceStore، لإظهار ما سيستخدمه المحلل فعلاً).
        var eligibilityShifts = Shifts.Where(s => s.Eligibility.Count > 0).OrderBy(s => s.Id).ToList();
        foreach (var row in Rows)
        {
            if (row.ShiftTypeId != null)
            {
                row.EffectiveSource = "Manual";
                row.EffectiveShiftName = row.ShiftName;
                row.EffectiveShiftColor = row.ShiftColor;
                continue;
            }
            var match = eligibilityShifts.FirstOrDefault(s =>
                ShiftTypeStore.EmployeeMatchesEligibility(s, row.EligibilityAttrs));
            if (match != null)
            {
                row.EffectiveSource = "Eligibility";
                row.EffectiveShiftName = match.Name;
                row.EffectiveShiftColor = match.ColorHex;
            }
        }
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        var employeeIds = ParseSelected();
        var shiftTypeId = int.TryParse(Request.Form["ShiftTypeId"], out var id) ? id : 0;

        if (employeeIds.Count == 0 || shiftTypeId <= 0)
        {
            TempData["SuccessMessage"] = "حدد موظفين واختر مناوبة أولاً.";
        }
        else
        {
            var count = await EmployeeShiftTypeStore.AssignAsync(_dbContext, employeeIds, shiftTypeId);
            TempData["SuccessMessage"] = $"تم تعيين المناوبة لـ{count} موظفاً.";
        }
        return RedirectToPage(new { Search, Filter });
    }

    public async Task<IActionResult> OnPostUnassignAsync()
    {
        var employeeIds = ParseSelected();
        if (employeeIds.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد موظفين أولاً.";
        }
        else
        {
            await EmployeeShiftTypeStore.UnassignAsync(_dbContext, employeeIds);
            TempData["SuccessMessage"] = $"أُلغي تعيين {employeeIds.Count} موظفاً (يرجعون للمناوبة الافتراضية).";
        }
        return RedirectToPage(new { Search, Filter });
    }

    private List<int> ParseSelected() =>
        Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
}
