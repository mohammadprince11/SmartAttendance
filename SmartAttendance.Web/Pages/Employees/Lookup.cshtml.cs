using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
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

    /// <summary>
    /// صفّ النتيجة — الأعمدة الأربعة المعروضة، مع معرّفات النطاق (شركة/فرع/قسم)
    /// للفحص الداخليّ فقط؛ لا تُعاد في الـJSON (يُبنى بمعطياتٍ صريحة).
    /// </summary>
    private sealed record Row(
        int id, string code, string name, string unit, string hierarchy,
        int companyId, int branchId, int departmentId);

    /// <param name="includeInactive">
    /// يشمل منتهيَ الخدمة. لا يوسّع التخويل بحال — نطاق المستخدم مفروضٌ بعده كما
    /// هو؛ يوسّع الحالة فقط، لشاشات ما بعد الإنهاء التي موضوعها من أُنهيت خدمته.
    /// </param>
    public async Task<IActionResult> OnGetAsync(string? q, int take = 50, bool includeInactive = false)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        var canView = await _permissions.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        if (!canView)
        {
            return Forbid();
        }

        // نطاق «موظفي أدوار الوصول» — يُقاطَع (AND) مع نطاق القواعد صفّاً-صفّاً أدناه،
        // كما بقائمة الأشخاص (P0-1): من نطاق أدواره أضيق من قواعده لا يجد بالمنتقي من
        // لا يراه بالقائمة. أدمن/«All» يُحسَم غير مقيَّد فلا يضيّق.
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        // الحدّ الأعلى مقيَّد بالكود لا بالمعطى: طلبٌ بـtake=100000 يجعل النقطة
        // تصديراً كاملاً لدليل الموظفين بنداء واحد.
        take = Math.Clamp(take, 1, 200);

        var term = (q ?? string.Empty).Trim();
        var like = term.Length == 0 ? null : "%" + term + "%";
        var filter = like is null
            ? string.Empty
            : " AND (e.FullName LIKE @Q OR e.EmployeeNo LIKE @Q)";

        var activeFilter = includeInactive ? string.Empty : " AND ISNULL(e.IsActive, 1) = 1";

        // نقرأ صفّاً زائداً واحداً كي نعرف **أن** هناك مزيداً بلا عدّ الجدول كلّه:
        // العدّاد الذي يعرض حجم الصفحة على أنه المجموع يكذب (1356 موظفاً ⟵ «(50)»).
        var probe = take + 1;

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            $"""
SELECT TOP (@Take)
       e.Id,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       ISNULL(e.FullName, N'')   AS FullName,
       ISNULL(b.Name, N'')       AS UnitName,
       ISNULL(d.Name, N'')       AS DeptName,
       ISNULL(b.CompanyId, 0)    AS CompanyId,
       ISNULL(e.BranchId, 0)     AS BranchId,
       ISNULL(e.DepartmentId, 0) AS DepartmentId
FROM Employees e
LEFT JOIN Branches b     ON b.Id = e.BranchId
LEFT JOIN Departments d  ON d.Id = e.DepartmentId
WHERE ISNULL(e.IsDeleted, 0) = 0{activeFilter}{filter}
ORDER BY e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Take", probe);
                if (like is not null) HrmsDatabase.AddParameter(command, "@Q", like);
            },
            reader => new Row(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "FullName"),
                HrmsDatabase.GetString(reader, "UnitName"),
                HrmsDatabase.GetString(reader, "DeptName"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetInt(reader, "BranchId"),
                HrmsDatabase.GetInt(reader, "DepartmentId")));

        // القصْر لنطاق المستخدم **بعد** القراءة وقبل الإرجاع: من لا يرى موظفاً
        // بقائمة الموظفين لا يجده بالمنتقي.
        var allowed = new List<Row>(rows.Count);
        foreach (var row in rows)
        {
            // تقاطع (AND): نطاق القواعد (CanAccessEmployeeAsync) ونطاق أدوار الوصول.
            var rulesAllows = await _permissions.CanAccessEmployeeAsync(
                systemUserId,
                PeoplePermissionCodes.ViewDirectory,
                row.id,
                PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
                HttpContext.RequestAborted);

            if (rulesAllows &&
                accessRoleScope.AllowsEmployee(row.id, row.companyId, row.branchId, row.departmentId))
            {
                allowed.Add(row);
            }
        }

        // `capped` ⟺ الاستعلام أعاد الصفّ الزائد ⟹ ثمّة مطابقات لم تُعرض.
        // العدّاد بالواجهة يقول عندها «+50» لا «50»، فلا يدّعي مجموعاً ليس مجموعاً.
        var capped = rows.Count > take;
        if (allowed.Count > take)
        {
            allowed.RemoveRange(take, allowed.Count - take);
        }

        // إسقاطٌ صريح للأعمدة الأربعة المعروضة فقط — معرّفات النطاق داخليّة لا تُعاد.
        var items = allowed
            .Select(r => new { r.id, r.code, r.name, r.unit, r.hierarchy })
            .ToList();

        return new JsonResult(new { total = items.Count, capped, items });
    }
}
