using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.ShiftAssignments;

/// <summary>
/// مناوبات الموظفين الثابتة (/ShiftAssignments) — المرحلة 5 من مودل الحضور بنمط
/// كيان («تحديد مناوبات الموظفين» الجماعي): بحث + تحديد متعدد + تعيين/إلغاء
/// مناوبة ShiftType. المحلل يستخدم هذا التعيين. راجع قسمي 13 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
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
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        // منتقي المناوبات معروضٌ ومُختار منه ⟹ يُحصر بالنطاق (المشترك + شركاتي).
        // الاحتساب لا يمرّ من هنا، فلا يتأثّر المسير.
        Shifts = (await ShiftTypeStore.ListInScopeAsync(_dbContext, scope)).Where(s => s.IsActive).ToList();

        var all = await EmployeeShiftTypeStore.ListAsync(_dbContext);

        // حصر السرد على موظفي شركات المستخدم: المتجر يسرد كل الشركات، فبلا هذا الحصر
        // يقرأ مستخدم شركة A أسماء وأرقام موظفي B ويعيّن لهم مناوبات.
        if (!scope.IsUnrestricted)
        {
            var allowedIds = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, all.Select(r => r.EmployeeId), scope, HttpContext.RequestAborted);
            all = all.Where(r => allowedIds.Contains(r.EmployeeId)).ToList();
        }

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
            // حارس التحديد الجماعي: أي معرّف موظف خارج نطاق شركاتي (نموذج معدَّل يدوياً)
            // يرفض الدفعة كلها — لا كتابة عابرة للشركات.
            if (!await AllInScopeAsync(employeeIds)) return NotFound();

            // المناوبة نفسها مرجعٌ من النموذج: بلا حارسها يُسند مستخدمُ شركةٍ نوعَ
            // مناوبة شركةٍ أخرى لموظفيه — ولا يراه أحدٌ بشاشة تلك الشركة.
            if (!await ConfigTenantScope.AreAllInScopeAsync(
                    _dbContext,
                    ConfigTenantScope.ShiftTypes,
                    new[] { shiftTypeId },
                    await _companyScope.GetAsync(HttpContext.RequestAborted)))
            {
                return NotFound();
            }

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
            if (!await AllInScopeAsync(employeeIds)) return NotFound();

            await EmployeeShiftTypeStore.UnassignAsync(_dbContext, employeeIds);
            TempData["SuccessMessage"] = $"أُلغي تعيين {employeeIds.Count} موظفاً (يرجعون للمناوبة الافتراضية).";
        }
        return RedirectToPage(new { Search, Filter });
    }

    /// <summary>كل المعرّفات المطلوبة ضمن نطاق شركاتي؟ (مغلق الفشل — أي نقصٍ ⟹ false).</summary>
    private async Task<bool> AllInScopeAsync(List<int> employeeIds)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
            _dbContext, employeeIds, scope, HttpContext.RequestAborted);
        return allowed.Count == employeeIds.Count;
    }

    private List<int> ParseSelected() =>
        Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
}
