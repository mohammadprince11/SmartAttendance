using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// نقطة بحث الموظفين التي يستهلكها منتقي الموظف المشترك (نظير نافذة «بحث عن
/// الموظف» بكيان: رمز · اسم · وحدة عمل · الهيكلية + عدّاد نتائج).
///
/// **لماذا نقطة لا منسدلة؟** شاشاتنا كانت تحمّل **1356 موظفاً بكل فتحة صفحة**
/// داخل `&lt;select&gt;`. كيان لا يحمّل أحداً حتى تُفتح النافذة، فالبحث الخادمي
/// ليس تجميلاً بل مكسب أداء حقيقي.
///
/// ⚠️ **نطاق التخويل مفروضٌ هنا** لا بالواجهة: نقطةٌ ترجع كل الموظفين لمن يملك
/// رابطها تلتفّ على كل ضوابط الوصول بالنظام. القصْر بنفس `EmployeeScopeFilter`
/// الذي تستعمله قائمة الموظفين، فما لا تراه بالقائمة لا تجده هنا.
/// </summary>
public class LookupModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionAuthorizationService _permissions;
    private readonly IEffectiveScopeService _effectiveScopeService;

    public LookupModel(
        ApplicationDbContext db,
        IPermissionAuthorizationService permissions,
        IEffectiveScopeService effectiveScopeService)
    {
        _db = db;
        _permissions = permissions;
        _effectiveScopeService = effectiveScopeService;
    }

    /// <param name="includeInactive">
    /// يشمل منتهيَ الخدمة. لا يوسّع التخويل بحال — نطاق المستخدم مفروضٌ بعده كما
    /// هو؛ يوسّع الحالة فقط، لشاشات ما بعد الإنهاء التي موضوعها من أُنهيت خدمته.
    /// </param>
    public async Task<IActionResult> OnGetAsync(string? q, int take = 50, bool includeInactive = false)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var compatibilityAllowed = PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory);

        var canView = await _permissions.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            compatibilityAllowed,
            HttpContext.RequestAborted);

        if (!canView)
        {
            return Forbid();
        }

        // النطاقان يُطبَّقان **داخل SQL قبل TOP** لا صفّاً-صفّاً بعده:
        //   نطاق القواعد (People Permission Scope) ∩ نطاق أدوار الوصول (Access Role)
        // بإعادة استعمال ApplyPeopleDataScope مرّتين (تقاطع AND) — نفس منطق قائمة
        // الأشخاص (P0-1) حرفاً بحرف، لا نموذجاً ثانياً. **لماذا قبل TOP؟** لأن الترشيح
        // بعد الحدّ كان يبتلع صفوفاً مرفوضة ضمن الخمسين، فيحرم المستخدم من مخوّلين
        // خلفها (قد يرى صفراً وله ألف). والقصْر داخل SQL يزيل أيضاً حلقة N+1
        // (تخويلٌ لكل صفّ على حدة بعد القراءة).
        var rulesScope = await _permissions.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            compatibilityAllowed,
            HttpContext.RequestAborted);

        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        // الحدّ الأعلى مقيَّد بالكود لا بالمعطى: طلبٌ بـtake=100000 يجعل النقطة
        // تصديراً كاملاً لدليل الموظفين بنداء واحد.
        take = Math.Clamp(take, 1, 200);

        var term = (q ?? string.Empty).Trim();

        var query = _db.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted)
            .ApplyPeopleDataScope(rulesScope)
            .ApplyPeopleDataScope(accessRoleScope);

        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        if (term.Length > 0)
        {
            query = query.Where(e => e.FullName.Contains(term) || e.EmployeeNo.Contains(term));
        }

        // نقرأ صفّاً زائداً واحداً كي نعرف **أن** هناك مزيداً بلا عدّ الجدول كلّه:
        // العدّاد الذي يعرض حجم الصفحة على أنه المجموع يكذب (1356 موظفاً ⟵ «(50)»).
        var probe = take + 1;

        var rows = await query
            .OrderBy(e => e.EmployeeNo)
            .Take(probe)
            .Select(e => new
            {
                id = e.Id,
                code = e.EmployeeNo ?? string.Empty,
                name = e.FullName ?? string.Empty,
                unit = e.Branch.Name ?? string.Empty,
                hierarchy = e.Department.Name ?? string.Empty
            })
            .ToListAsync(HttpContext.RequestAborted);

        // `capped` ⟺ الاستعلام أعاد الصفّ الزائد ⟹ ثمّة مطابقات مخوّلة لم تُعرض.
        // العدّاد بالواجهة يقول عندها «+50» لا «50»، فلا يدّعي مجموعاً ليس مجموعاً.
        var capped = rows.Count > take;
        var items = capped ? rows.Take(take).ToList() : rows;

        return new JsonResult(new { total = items.Count, capped, items });
    }
}
