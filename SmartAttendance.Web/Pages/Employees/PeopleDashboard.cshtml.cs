using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// داشبورد الأشخاص (نمط كيان قسم 18.7): مؤشرات المودل المتخصصة التي لا تظهر
/// باللوحة التنفيذية — جديد هذا الشهر، بفترة التجربة، أُنهي هذا الشهر،
/// معدل الدوران الطوعي/غير الطوعي بنطاق سنة، تكافؤ الجنسين، وشرائح مدة الخدمة.
///
/// P0-6 — كل التجميعات كانت تعدّ موظفي **كل الشركات** بلا نطاق (تسريب متعدّد
/// الشركات). الآن نحسب مجموعة معرّفات الموظفين المسموح بها بتقاطع نطاق القواعد
/// (ViewDirectory) مع نطاق أدوار الوصول، ونحصر كل استعلام بها. المسار السريع:
/// حين يكون النطاقان غير مقيَّدَين (أدمن نموذجيّاً) لا نُرشِّح إطلاقاً.
/// </summary>
public class PeopleDashboardModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private readonly IEffectiveScopeService _effectiveScopeService;

    public PeopleDashboardModel(
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissionAuthorizationService,
        IEffectiveScopeService effectiveScopeService)
    {
        _dbContext = dbContext;
        _permissionAuthorizationService = permissionAuthorizationService;
        _effectiveScopeService = effectiveScopeService;
    }

    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int NewThisMonth { get; set; }
    public int InProbation { get; set; }
    public int Suspended { get; set; }
    public int EndedThisMonth { get; set; }

    // معدل الدوران بنطاق سنة: استقالة = طوعي، غيره (فصل/إنهاء عقد...) = غير طوعي.
    public int VoluntaryExits { get; set; }
    public int InvoluntaryExits { get; set; }
    public double TurnoverRate { get; set; }
    public double VoluntaryRate { get; set; }

    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }

    public sealed record Bucket(string Label, int Count);
    public List<Bucket> TenureBuckets { get; set; } = new();
    public List<Bucket> AgeBuckets { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        var directoryScope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        // تقاطع نطاق أدوار الوصول مع نطاق القواعد: صفٌّ يُعدّ فقط إن سمح به الاثنان.
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        // حرمانٌ كليّ من أيّ نطاق ⟹ كل المؤشرات صفر (الكتالوج يمنع الوصول أصلاً، وهذا
        // دفاعٌ بالعمق: لا نكشف أرقاماً حتى لو وصل الطلب).
        if (directoryScope.IsDeniedAll || accessRoleScope.IsDeniedAll)
        {
            return;
        }

        var unrestricted =
            directoryScope.IsUnrestricted && !directoryScope.HasAnyDenial &&
            accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial;

        // مرشِّحان يُحقنان داخل الاستعلامات: على Employees (العمود Id) وعلى
        // EmployeeEndServices (العمود EmployeeId). القيم أعداد صحيحة فقط — لا حقن.
        var empFilter = string.Empty;
        var endSvcFilter = string.Empty;

        if (!unrestricted)
        {
            var allowedIds = await LoadAllowedEmployeeIdsAsync(directoryScope, accessRoleScope);
            if (allowedIds.Count == 0)
            {
                return;
            }

            var csv = string.Join(",", allowedIds);
            empFilter = $" AND Id IN ({csv})";
            endSvcFilter = $" AND EmployeeId IN ({csv})";
        }

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
DECLARE @MonthStart date = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
DECLARE @YearAgo date = DATEADD(year, -1, GETDATE());

SELECT
    TotalEmployees   = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0{empFilter}),
    ActiveEmployees  = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 1{empFilter}),
    NewThisMonth     = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND HireDate >= @MonthStart{empFilter}),
    InProbation      = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 1
                          AND DATEADD(day, 90, ISNULL(JoiningDate, HireDate)) >= GETDATE(){empFilter}),
    Suspended        = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 0{empFilter}),
    EndedThisMonth   = (SELECT COUNT(*) FROM EmployeeEndServices WHERE LastWorkingDate >= @MonthStart{endSvcFilter}),
    VoluntaryExits   = (SELECT COUNT(*) FROM EmployeeEndServices
                        WHERE LastWorkingDate >= @YearAgo
                          AND (EndServiceType LIKE N'%استقال%' OR EndServiceTypeText LIKE N'%استقال%'){endSvcFilter}),
    InvoluntaryExits = (SELECT COUNT(*) FROM EmployeeEndServices
                        WHERE LastWorkingDate >= @YearAgo
                          AND NOT (EndServiceType LIKE N'%استقال%' OR EndServiceTypeText LIKE N'%استقال%'){endSvcFilter}),
    MaleCount        = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND Gender = 'Male'{empFilter}),
    FemaleCount      = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND Gender = 'Female'{empFilter});
""",
            command => { },
            reader => new
            {
                TotalEmployees = HrmsDatabase.GetInt(reader, "TotalEmployees"),
                ActiveEmployees = HrmsDatabase.GetInt(reader, "ActiveEmployees"),
                NewThisMonth = HrmsDatabase.GetInt(reader, "NewThisMonth"),
                InProbation = HrmsDatabase.GetInt(reader, "InProbation"),
                Suspended = HrmsDatabase.GetInt(reader, "Suspended"),
                EndedThisMonth = HrmsDatabase.GetInt(reader, "EndedThisMonth"),
                VoluntaryExits = HrmsDatabase.GetInt(reader, "VoluntaryExits"),
                InvoluntaryExits = HrmsDatabase.GetInt(reader, "InvoluntaryExits"),
                MaleCount = HrmsDatabase.GetInt(reader, "MaleCount"),
                FemaleCount = HrmsDatabase.GetInt(reader, "FemaleCount")
            });

        var stats = rows.FirstOrDefault();
        if (stats == null) return;

        TotalEmployees = stats.TotalEmployees;
        ActiveEmployees = stats.ActiveEmployees;
        NewThisMonth = stats.NewThisMonth;
        InProbation = stats.InProbation;
        Suspended = stats.Suspended;
        EndedThisMonth = stats.EndedThisMonth;
        VoluntaryExits = stats.VoluntaryExits;
        InvoluntaryExits = stats.InvoluntaryExits;
        MaleCount = stats.MaleCount;
        FemaleCount = stats.FemaleCount;

        // معدل الدوران = المغادرون خلال سنة ÷ متوسط قوة العمل (تقريب: الفعالون الحاليون).
        var exits = VoluntaryExits + InvoluntaryExits;
        var basis = Math.Max(1, ActiveEmployees);
        TurnoverRate = Math.Round(exits * 100.0 / basis, 1);
        VoluntaryRate = Math.Round(VoluntaryExits * 100.0 / basis, 1);

        TenureBuckets = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT Label, COUNT(*) AS Count FROM
(
    SELECT CASE
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 1 THEN N'أقل من سنة'
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 3 THEN N'1 – 3 سنوات'
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 5 THEN N'3 – 5 سنوات'
        ELSE N'أكثر من 5 سنوات' END AS Label
    FROM Employees WHERE IsDeleted = 0 AND IsActive = 1{empFilter}
) t GROUP BY Label
ORDER BY MIN(CASE Label WHEN N'أقل من سنة' THEN 1 WHEN N'1 – 3 سنوات' THEN 2 WHEN N'3 – 5 سنوات' THEN 3 ELSE 4 END);
""",
            command => { },
            reader => new Bucket(HrmsDatabase.GetString(reader, "Label"), HrmsDatabase.GetInt(reader, "Count")));

        AgeBuckets = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT Label, COUNT(*) AS Count FROM
(
    SELECT CASE
        WHEN BirthDate IS NULL THEN N'غير محدد'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 25 THEN N'أقل من 25'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 35 THEN N'25 – 35'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 45 THEN N'35 – 45'
        ELSE N'45 فأكثر' END AS Label
    FROM Employees WHERE IsDeleted = 0 AND IsActive = 1{empFilter}
) t GROUP BY Label
ORDER BY MIN(CASE Label WHEN N'أقل من 25' THEN 1 WHEN N'25 – 35' THEN 2 WHEN N'35 – 45' THEN 3 WHEN N'45 فأكثر' THEN 4 ELSE 5 END);
""",
            command => { },
            reader => new Bucket(HrmsDatabase.GetString(reader, "Label"), HrmsDatabase.GetInt(reader, "Count")));
    }

    /// <summary>
    /// يحمّل معرّفات الموظفين التي يسمح بها **كلا** النطاقين. الترشيح بلغة C# عبر
    /// نفس <see cref="PeopleDataScope.AllowsEmployee"/> المستعملة بصفحة السرد، فلا
    /// نُعيد كتابة منطق النطاق بالـSQL (شركة · فرع · قسم · موظف · استثناءات).
    /// </summary>
    private async Task<List<int>> LoadAllowedEmployeeIdsAsync(
        PeopleDataScope directoryScope,
        PeopleDataScope accessRoleScope)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT e.Id, b.CompanyId, e.BranchId, ISNULL(e.DepartmentId, 0) AS DepartmentId
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0;
""",
            command => { },
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId")
            });

        return rows
            .Where(r =>
                directoryScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId) &&
                accessRoleScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId))
            .Select(r => r.Id)
            .ToList();
    }
}
