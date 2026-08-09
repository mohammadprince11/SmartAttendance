using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// تخصيص رئيس وحدة مؤقت — إجازة مديرٍ كانت تجمّد كل ما يُوجَّه إليه.
/// المنطق كلّه بـ<see cref="TemporaryHeadPolicy"/>، وهذه الصفحة إدخالٌ وعرض فقط.
///
/// P0-5 — كانت الصفحة تسرد **وحدات كل الشركات** وكل الإسنادات بلا نطاق، وتحفظ
/// أيّ وحدة/رئيس بلا تحقّق. الآن تُحصر القوائم بالشركات التي يملك المستخدم موظفاً
/// ضمنها (تقاطع نطاق القواعد مع أدوار الوصول)، والحفظ يرفض وحدةً أو رئيساً خارج
/// النطاق. المسار السريع: نطاقٌ غير مقيَّد ⟹ بلا ترشيح (أدمن نموذجيّاً).
/// </summary>
public class TemporaryHeadsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private readonly IEffectiveScopeService _effectiveScopeService;

    public TemporaryHeadsModel(
        ApplicationDbContext db,
        IPermissionAuthorizationService permissionAuthorizationService,
        IEffectiveScopeService effectiveScopeService)
    {
        _db = db;
        _permissionAuthorizationService = permissionAuthorizationService;
        _effectiveScopeService = effectiveScopeService;
    }

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }

    [BindProperty] public int AllocationId { get; set; }
    [BindProperty] public int DepartmentId { get; set; }
    [BindProperty] public int HeadEmployeeId { get; set; }
    [BindProperty] public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string? Note { get; set; }

    public List<TemporaryHeadStore.Row> Allocations { get; private set; } = new();
    public List<(int Id, string Name)> Departments { get; private set; } = new();

    /// <summary>رمز الرئيس المؤقت المحسوم واسمه — لتعبئة المنتقي ابتداءً.</summary>
    public string? HeadEmployeeCode { get; private set; }

    public string? HeadEmployeeName { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public int ActiveNow => Allocations.Count(row => TemporaryHeadPolicy.IsEffective(row.ToAllocation(), Today));

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && Allocations.FirstOrDefault(row => row.Id == EditingId.Value) is { } row)
        {
            AllocationId = row.Id;
            DepartmentId = row.DepartmentId;
            HeadEmployeeId = row.HeadEmployeeId;
            FromDate = row.FromDate;
            ToDate = row.ToDate;
            IsActive = row.IsActive;
            Note = row.Note;
        }

        await LoadPickerIdentityAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (DepartmentId <= 0 || HeadEmployeeId <= 0)
        {
            TempData["ErrorMessage"] = "الوحدة والرئيس المؤقت إلزاميان.";
            return RedirectToPage();
        }

        // فحص النطاق (سيرفر): الوحدة والرئيس المؤقت يجب أن يقعا ضمن نطاق المستخدم —
        // وإلا أمكن إسناد رئيسٍ من شركةٍ أخرى أو على وحدةٍ لا يملكها (P0-5).
        var scope = await ResolveScopeAsync();
        if (!scope.Unrestricted)
        {
            if (!scope.AllowedDepartmentIds.Contains(DepartmentId))
            {
                TempData["ErrorMessage"] = "الوحدة المختارة خارج نطاق صلاحياتك.";
                return RedirectToPage();
            }

            if (!scope.AllowedEmployeeIds.Contains(HeadEmployeeId))
            {
                TempData["ErrorMessage"] = "الرئيس المؤقت المختار خارج نطاق صلاحياتك.";
                return RedirectToPage();
            }
        }

        // مدىً مقلوب يُحفظ بصمت ولا يسري أبداً — عطل صامت، فيُرفض هنا.
        if (!TemporaryHeadPolicy.IsValidRange(FromDate, ToDate))
        {
            TempData["ErrorMessage"] = "تاريخ النهاية قبل البداية — الإسناد لن يسري أبداً.";
            return RedirectToPage();
        }

        // المعرّف المُعاد لازم لفحص التداخل: بإسنادٍ جديد يبقى AllocationId صفراً،
        // فيرى الصفُّ المحفوظ نفسَه «إسناداً آخر» ويحذّر من تداخله مع ذاته دائماً.
        var savedId = await TemporaryHeadStore.SaveAsync(
            _db, AllocationId, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive, Note, User.Identity?.Name);

        var overlaps = TemporaryHeadPolicy.Overlapping(
            new TemporaryHeadPolicy.Allocation(savedId, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive),
            (await TemporaryHeadStore.LoadAsync(_db)).Select(row => row.ToAllocation()));

        TempData["SuccessMessage"] = overlaps.Count > 0
            ? $"تم الحفظ — تنبيه: يتداخل مع {overlaps.Count} إسناداً لنفس الوحدة، والأحدث هو الذي يسري."
            : "تم حفظ الإسناد.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // الحذف يُقيَّد بالإسنادات الواقعة ضمن نطاق المستخدم فقط.
        var scope = await ResolveScopeAsync();
        if (!scope.Unrestricted)
        {
            var target = (await TemporaryHeadStore.LoadAsync(_db)).FirstOrDefault(r => r.Id == id);
            if (target is null || !scope.AllowedDepartmentIds.Contains(target.DepartmentId))
            {
                TempData["ErrorMessage"] = "الإسناد خارج نطاق صلاحياتك.";
                return RedirectToPage();
            }
        }

        await TemporaryHeadStore.DeleteAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف الإسناد.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var scope = await ResolveScopeAsync();

        var all = await TemporaryHeadStore.LoadAsync(_db);
        Allocations = scope.Unrestricted
            ? all
            : all.Where(row => scope.AllowedDepartmentIds.Contains(row.DepartmentId)).ToList();

        var departments = await HrmsDatabase.QueryAsync(
            _db,
            "SELECT Id, ISNULL(Name, N'') AS Name FROM Departments WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY Name;",
            command => { },
            reader => (Id: HrmsDatabase.GetInt(reader, "Id"), Name: HrmsDatabase.GetString(reader, "Name")));

        Departments = (scope.Unrestricted
                ? departments
                : departments.Where(d => scope.AllowedDepartmentIds.Contains(d.Id)))
            .ToList();
    }

    private async Task LoadPickerIdentityAsync()
    {
        var identity = await EmployeePickerLookup.LoadAsync(_db, HeadEmployeeId);
        HeadEmployeeCode = identity?.Code;
        HeadEmployeeName = identity?.Name;
    }

    private sealed record TemporaryHeadScope(
        bool Unrestricted,
        HashSet<int> AllowedDepartmentIds,
        HashSet<int> AllowedEmployeeIds);

    /// <summary>
    /// يحسم نطاق هذه الصفحة: تقاطع نطاق القواعد (ViewDirectory) مع نطاق أدوار الوصول.
    /// يُنتج مجموعتَي الوحدات والموظفين المسموح بهما بلغة C# عبر
    /// <see cref="PeopleDataScope.AllowsEmployee"/> — لا إعادة كتابة للنطاق بالـSQL.
    /// </summary>
    private async Task<TemporaryHeadScope> ResolveScopeAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        var directoryScope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        if (directoryScope.IsDeniedAll || accessRoleScope.IsDeniedAll)
        {
            return new TemporaryHeadScope(false, new HashSet<int>(), new HashSet<int>());
        }

        var unrestricted =
            directoryScope.IsUnrestricted && !directoryScope.HasAnyDenial &&
            accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial;

        if (unrestricted)
        {
            return new TemporaryHeadScope(true, new HashSet<int>(), new HashSet<int>());
        }

        var rows = await HrmsDatabase.QueryAsync(
            _db,
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

        var allowedEmployees = new HashSet<int>();
        var allowedDepartments = new HashSet<int>();

        foreach (var r in rows)
        {
            if (directoryScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId) &&
                accessRoleScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId))
            {
                allowedEmployees.Add(r.Id);
                if (r.DepartmentId > 0)
                {
                    allowedDepartments.Add(r.DepartmentId);
                }
            }
        }

        return new TemporaryHeadScope(false, allowedDepartments, allowedEmployees);
    }
}
