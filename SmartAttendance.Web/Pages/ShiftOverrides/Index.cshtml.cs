using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.ShiftOverrides;

/// <summary>
/// تعديل مناوبات مؤقت (/ShiftOverrides) — الصفحة الثانية بقسم «حضور الموظفين» (نمط
/// كيان): قائمة سجلات التجاوز (موظف ← مناوبة بديلة لفترة) + إنشاء لنطاق (الكل/محدد).
/// المحلل يعطي التجاوز الأولوية على التعيين الدائم لأيام الفترة.
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

    public record EmpLookup(int Id, string No, string Name);

    public List<ShiftOverrideStore.OverrideRow> Rows { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public List<EmpLookup> Employees { get; set; } = new();
    public int TotalRows { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        // منتقي المناوبات معروضٌ ومُختار منه ⟹ محصورٌ بالنطاق، لا بالاحتساب.
        Shifts = (await ShiftTypeStore.ListInScopeAsync(_dbContext, scope)).Where(s => s.IsActive).ToList();

        Employees = await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.EmployeeNo)
            .Select(e => new EmpLookup(e.Id, e.EmployeeNo, e.FullName)).ToListAsync();

        var all = await ShiftOverrideStore.ListAsync(_dbContext);

        // حصر منتقي الموظفين وسجلّات التجاوز على شركاتي — كلاهما كان يسرد كل الشركات.
        if (!scope.IsUnrestricted)
        {
            var empAllowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, Employees.Select(e => e.Id), scope, HttpContext.RequestAborted);
            Employees = Employees.Where(e => empAllowed.Contains(e.Id)).ToList();

            var rowAllowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, all.Select(r => r.EmployeeId), scope, HttpContext.RequestAborted);
            all = all.Where(r => rowAllowed.Contains(r.EmployeeId)).ToList();
        }

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

        var companyScope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        // المناوبة مرجعٌ من النموذج لا من المنتقي المحصور: تُتحقَّق قبل أي كتابة.
        if (!await ConfigTenantScope.AreAllInScopeAsync(
                _dbContext, ConfigTenantScope.ShiftTypes, new[] { shiftTypeId }, companyScope))
        {
            return NotFound();
        }

        List<int> employeeIds;
        if (scope == "All")
        {
            // «الكل» = كل النشطين ضمن **نطاق شركاتي** لا كل قاعدة البيانات — وإلا أنشأ
            // مستخدم شركة A تعديلاً على موظفي B. للأدمن (غير مقيَّد) تبقى الكلّ فعلاً.
            employeeIds = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.IsActive).Select(e => e.Id).ToListAsync();
            if (!companyScope.IsUnrestricted)
            {
                var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                    _dbContext, employeeIds, companyScope, HttpContext.RequestAborted);
                employeeIds = employeeIds.Where(allowed.Contains).ToList();
            }
        }
        else
        {
            employeeIds = form["EmployeeIds"]
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(id => id > 0).Distinct().ToList();

            // تحديد صريح: أي معرّف خارج نطاقي (نموذج معدَّل) يرفض الطلب كلّه.
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, employeeIds, companyScope, HttpContext.RequestAborted);
            if (allowed.Count != employeeIds.Count) return NotFound();
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
            // المعرّفات هنا صفوف تجاوز (لا موظفين): حارس ملكية عبر الموظف ← الشركة.
            var companyScope = await _companyScope.GetAsync(HttpContext.RequestAborted);
            var allowed = await EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync(
                _dbContext, EmployeeCompanyGuard.Tables.ShiftOverrides, "Id", ids,
                companyScope, HttpContext.RequestAborted);
            if (allowed.Count != ids.Count) return NotFound();

            await ShiftOverrideStore.DeleteAsync(_dbContext, ids);
            TempData["SuccessMessage"] = $"حُذف {ids.Count} تعديل مؤقت.";
        }
        return RedirectToPage(new { Search });
    }
}
