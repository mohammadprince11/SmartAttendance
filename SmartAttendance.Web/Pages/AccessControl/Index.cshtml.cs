using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.AccessControl;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAccessRoleService _accessRoleService;

    public IndexModel(ApplicationDbContext dbContext, IAccessRoleService accessRoleService)
    {
        _dbContext = dbContext;
        _accessRoleService = accessRoleService;
    }

    public sealed record UserOption(
        int Id, string UserName, string? FullName, string? Email,
        string SystemRole, int? EmployeeId);

    public sealed record LegacyPermissionRow(
        string Module, string Code, string Effect, string Scope,
        DateTime? ValidFromUtc, DateTime? ValidToUtc, bool IsActiveNow);

    public static readonly IReadOnlyList<(string Key, string Label)> Modes = new[]
    {
        ("Roles", "الأدوار والصلاحيات"),
        ("Assignments", "إسناد المستخدمين"),
        ("DataScope", "نطاق البيانات"),
        ("FieldSecurity", "الحقول الحساسة"),
        ("Effective", "الوصول الفعلي"),
    };

    public static readonly IReadOnlyList<(string Key, string Label)> RoleKinds = new[]
    {
        (AccessRoleStore.TypePages, "صلاحيات الصفحات"),
        (AccessRoleStore.TypeSelfService, "الخدمة الذاتية"),
        (AccessRoleStore.TypeReports, "التقارير"),
    };

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "Roles";

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = AccessRoleStore.TypePages;

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    public List<AccessRoleStore.AccessRole> Roles { get; set; } = new();
    public List<AccessRoleStore.AccessRole> AllRoles { get; set; } = new();
    public List<UserOption> Users { get; set; } = new();
    public Dictionary<int, List<int>> UserRoles { get; set; } = new();

    public AccessRoleStore.AccessRole? EditingRole { get; set; }
    public Dictionary<string, List<string>> EditingPageGrants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EditingDataScopes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EditingSensitiveFields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EditingSelfService { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EditingReports { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public UserOption? EffectiveUser { get; set; }
    public List<AccessRoleStore.AccessRole> EffectiveRoles { get; set; } = new();
    public AccessProfile? EffectiveProfile { get; set; }
    public HashSet<string> EffectiveSensitiveFields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EffectiveSelfService { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EffectiveReports { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<LegacyPermissionRow> LegacyDirectPermissions { get; set; } = new();

    public IReadOnlyList<PageCatalog.CatalogModule> Catalog => PageCatalog.Modules;
    public IReadOnlyList<DataScopeCatalog.DataEntity> DataEntities => DataScopeCatalog.Entities;
    public IReadOnlyList<DataScopeCatalog.ScopeLevel> ScopeLevels => DataScopeCatalog.ScopeLevels;
    public IReadOnlyList<SensitiveFieldCatalog.SensitiveField> SensitiveFields => SensitiveFieldCatalog.Fields;
    public IReadOnlyList<SelfServiceCatalog.SelfServiceAction> SelfServiceActions => SelfServiceCatalog.Actions;
    public IReadOnlyList<ReportsCatalog.ReportGroup> ReportGroups => ReportsCatalog.Groups;

    public string CurrentRoleType => Mode switch
    {
        "DataScope" => AccessRoleStore.TypeData,
        "FieldSecurity" => AccessRoleStore.TypeSensitiveFields,
        _ when RoleKinds.Any(x => x.Key.Equals(Type, StringComparison.OrdinalIgnoreCase)) => Type,
        _ => AccessRoleStore.TypePages,
    };

    public int TotalRoleCount => AllRoles.Count;
    public int ActiveRoleCount => AllRoles.Count(r => r.IsActive);
    public int AssignedUserCount => UserRoles.Count(x => x.Value.Count > 0);
    public int UserCount => Users.Count;

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeNavigation();
        await LoadBaseAsync();
        await LoadEditorAsync();

        if (Mode == "Effective" && UserId.GetValueOrDefault() > 0)
        {
            await LoadEffectiveAsync(UserId!.Value);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveRoleAsync()
    {
        var form = await Request.ReadFormAsync();
        var roleType = form["RoleType"].ToString().Trim();
        if (!AccessRoleStore.RoleTypes.Contains(roleType))
        {
            TempData["AccessControlError"] = "نوع الدور غير صالح.";
            return RedirectToPage();
        }

        var id = int.TryParse(form["Id"], out var parsedId) ? parsedId : 0;
        var existing = id > 0 ? await AccessRoleStore.GetAsync(_dbContext, id) : null;
        if (id > 0 && existing is null)
        {
            TempData["AccessControlError"] = "الدور المطلوب غير موجود.";
            return RedirectToRoleType(roleType);
        }

        if (existing is not null)
        {
            roleType = existing.RoleType;
        }

        var nameAr = form["NameAr"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(nameAr))
        {
            TempData["AccessControlError"] = "اسم الدور مطلوب.";
            return RedirectToRoleType(roleType, id > 0 ? id : null);
        }

        var role = new AccessRoleStore.AccessRole
        {
            Id = id,
            RoleType = roleType,
            NameAr = nameAr,
            NameEn = NullIfEmpty(form["NameEn"]),
            Note = NullIfEmpty(form["Note"]),
            IsActive = IsChecked(form, "IsActive"),
        };

        var savedId = await AccessRoleStore.SaveAsync(_dbContext, role);
        var grants = roleType switch
        {
            AccessRoleStore.TypePages => BuildPageGrants(form),
            AccessRoleStore.TypeData => BuildDataGrants(form),
            AccessRoleStore.TypeSensitiveFields => BuildSensitiveGrants(form),
            AccessRoleStore.TypeSelfService => BuildKeyGrants(
                form, "ss_", SelfServiceCatalog.Actions.Select(x => x.Code)),
            AccessRoleStore.TypeReports => BuildKeyGrants(
                form, "rep_", ReportsCatalog.Groups.Select(x => x.Code)),
            _ => new List<AccessRoleStore.AccessRoleGrant>(),
        };

        await AccessRoleStore.ReplaceGrantsAsync(_dbContext, savedId, grants);
        TempData["AccessControlMessage"] = id > 0
            ? "تم تحديث الدور والصلاحيات."
            : "تم إنشاء الدور والصلاحيات.";
        return RedirectToRoleType(roleType, savedId);
    }

    public async Task<IActionResult> OnPostToggleRoleAsync(int id)
    {
        var role = await AccessRoleStore.GetAsync(_dbContext, id);
        if (role is null) return NotFound();
        await AccessRoleStore.ToggleActiveAsync(_dbContext, id);
        TempData["AccessControlMessage"] = role.IsActive ? "تم إيقاف الدور." : "تم تفعيل الدور.";
        return RedirectToRoleType(role.RoleType);
    }

    public async Task<IActionResult> OnPostDeleteRoleAsync(int id)
    {
        var role = await AccessRoleStore.GetAsync(_dbContext, id);
        if (role is null) return NotFound();

        if (role.AffectedUsers > 0)
        {
            TempData["AccessControlError"] =
                $"لا يمكن حذف الدور لأنه مسند إلى {role.AffectedUsers} مستخدم. أزل الإسناد أولاً.";
            return RedirectToRoleType(role.RoleType, id);
        }

        await AccessRoleStore.DeleteAsync(_dbContext, id);
        TempData["AccessControlMessage"] = "تم حذف الدور.";
        return RedirectToRoleType(role.RoleType);
    }

    public async Task<IActionResult> OnPostSaveAssignmentsAsync(int systemUserId)
    {
        if (!await _dbContext.SystemUsers.AsNoTracking()
                .AnyAsync(u => u.Id == systemUserId && !u.IsDeleted))
        {
            TempData["AccessControlError"] = "المستخدم غير موجود.";
            return RedirectToPage(new { Mode = "Assignments" });
        }

        var form = await Request.ReadFormAsync();
        var requestedIds = form["RoleIds"]
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var activeIds = (await AccessRoleStore.ListActiveAsync(_dbContext))
            .Select(r => r.Id)
            .ToHashSet();
        var safeIds = requestedIds.Where(activeIds.Contains).ToList();

        await AccessRoleStore.ReplaceUserRolesAsync(_dbContext, systemUserId, safeIds);
        TempData["AccessControlMessage"] = "تم تحديث أدوار المستخدم.";
        return RedirectToPage(new { Mode = "Assignments", UserId = systemUserId });
    }

    private void NormalizeNavigation()
    {
        if (!Modes.Any(x => x.Key.Equals(Mode, StringComparison.OrdinalIgnoreCase)))
        {
            Mode = "Roles";
        }

        Mode = Modes.First(x => x.Key.Equals(Mode, StringComparison.OrdinalIgnoreCase)).Key;
        if (Mode == "Roles" && !RoleKinds.Any(x => x.Key.Equals(Type, StringComparison.OrdinalIgnoreCase)))
        {
            Type = AccessRoleStore.TypePages;
        }

        if (Mode == "DataScope") Type = AccessRoleStore.TypeData;
        if (Mode == "FieldSecurity") Type = AccessRoleStore.TypeSensitiveFields;
    }

    private async Task LoadBaseAsync()
    {
        await AccessRoleStore.EnsureAsync(_dbContext);
        AllRoles = new List<AccessRoleStore.AccessRole>();
        foreach (var roleType in AccessRoleStore.RoleTypes)
        {
            AllRoles.AddRange(await AccessRoleStore.ListAsync(_dbContext, roleType));
        }

        Roles = AllRoles
            .Where(r => r.RoleType.Equals(CurrentRoleType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Id)
            .ToList();

        var rawUsers = await _dbContext.SystemUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        Users = rawUsers.Select(u => new UserOption(
                u.Id, u.UserName, u.FullName, u.Email, u.Role.ToString(), u.EmployeeId))
            .ToList();

        UserRoles = await AccessRoleStore.GetRolesForUsersAsync(
            _dbContext, Users.Select(u => u.Id).ToList());

        if (Mode == "Assignments" && !UserId.HasValue && Users.Count > 0)
        {
            UserId = Users[0].Id;
        }
    }

    private async Task LoadEditorAsync()
    {
        if (!EditId.HasValue || EditId.Value <= 0)
        {
            return;
        }

        EditingRole = await AccessRoleStore.GetAsync(_dbContext, EditId.Value);
        if (EditingRole is null ||
            !EditingRole.RoleType.Equals(CurrentRoleType, StringComparison.OrdinalIgnoreCase))
        {
            EditingRole = null;
            return;
        }

        var grants = await AccessRoleStore.GetGrantsAsync(_dbContext, EditingRole.Id);

        if (EditingRole.RoleType == AccessRoleStore.TypePages)
        {
            EditingPageGrants = grants.ToDictionary(
                g => g.GrantKey,
                g => DeserializeActions(g.Payload),
                StringComparer.OrdinalIgnoreCase);
        }
        else if (EditingRole.RoleType == AccessRoleStore.TypeData)
        {
            EditingDataScopes = grants.ToDictionary(
                g => g.GrantKey,
                g => DeserializeScope(g.Payload),
                StringComparer.OrdinalIgnoreCase);
        }
        else if (EditingRole.RoleType == AccessRoleStore.TypeSensitiveFields)
        {
            EditingSensitiveFields = grants.Select(g => g.GrantKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else if (EditingRole.RoleType == AccessRoleStore.TypeSelfService)
        {
            EditingSelfService = grants.Select(g => g.GrantKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else if (EditingRole.RoleType == AccessRoleStore.TypeReports)
        {
            EditingReports = grants.Select(g => g.GrantKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task LoadEffectiveAsync(int systemUserId)
    {
        EffectiveUser = Users.FirstOrDefault(u => u.Id == systemUserId);
        if (EffectiveUser is null) return;

        var assignedIds = UserRoles.TryGetValue(systemUserId, out var ids)
            ? ids.ToHashSet()
            : new HashSet<int>();

        EffectiveRoles = AllRoles
            .Where(r => assignedIds.Contains(r.Id) && r.IsActive)
            .OrderBy(r => r.RoleType)
            .ThenBy(r => r.NameAr)
            .ToList();

        EffectiveProfile = await _accessRoleService.ResolveAsync(systemUserId, HttpContext.RequestAborted);

        EffectiveSensitiveFields = (await AccessRoleStore.GetUserGrantsAsync(
                _dbContext, systemUserId, AccessRoleStore.TypeSensitiveFields))
            .Select(g => g.GrantKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        EffectiveSelfService = (await AccessRoleStore.GetUserGrantsAsync(
                _dbContext, systemUserId, AccessRoleStore.TypeSelfService))
            .Select(g => g.GrantKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        EffectiveReports = (await AccessRoleStore.GetUserGrantsAsync(
                _dbContext, systemUserId, AccessRoleStore.TypeReports))
            .Select(g => g.GrantKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await LoadLegacyDirectPermissionsAsync(systemUserId);
    }

    private async Task LoadLegacyDirectPermissionsAsync(int systemUserId)
    {
        var now = DateTime.UtcNow;
        var rows = await _dbContext.SystemUserPermissions.AsNoTracking()
            .Include(x => x.Permission)
            .Where(x => x.SystemUserId == systemUserId && !x.IsDeleted)
            .ToListAsync();

        LegacyDirectPermissions = rows.Select(x => new LegacyPermissionRow(
                x.Permission.Module,
                x.Permission.Code,
                x.Effect.ToString(),
                BuildLegacyScope(x.ScopeType.ToString(), x.ScopeCompanyId, x.ScopeBranchId,
                    x.ScopeDepartmentId, x.ScopeEmployeeId),
                x.ValidFromUtc,
                x.ValidToUtc,
                (!x.ValidFromUtc.HasValue || x.ValidFromUtc <= now) &&
                (!x.ValidToUtc.HasValue || x.ValidToUtc >= now)))
            .OrderBy(x => x.Module)
            .ThenBy(x => x.Code)
            .ToList();
    }

    private IActionResult RedirectToRoleType(string roleType, int? editId = null)
    {
        var mode = roleType switch
        {
            AccessRoleStore.TypeData => "DataScope",
            AccessRoleStore.TypeSensitiveFields => "FieldSecurity",
            _ => "Roles",
        };

        return RedirectToPage(new
        {
            Mode = mode,
            Type = roleType,
            EditId = editId,
        });
    }

    private static string BuildLegacyScope(
        string type, int? companyId, int? branchId, int? departmentId, int? employeeId)
    {
        var parts = new List<string> { type };
        if (companyId.HasValue) parts.Add($"Company:{companyId}");
        if (branchId.HasValue) parts.Add($"Branch:{branchId}");
        if (departmentId.HasValue) parts.Add($"Department:{departmentId}");
        if (employeeId.HasValue) parts.Add($"Employee:{employeeId}");
        return string.Join(" · ", parts);
    }

    private static List<AccessRoleStore.AccessRoleGrant> BuildPageGrants(IFormCollection form)
    {
        var grants = new List<AccessRoleStore.AccessRoleGrant>();
        foreach (var module in PageCatalog.Modules)
        {
            foreach (var page in module.Pages)
            {
                var actions = PageCatalog.Actions
                    .Where(action => IsChecked(form, $"grant_{page.Code}_{action}"))
                    .ToList();

                if (actions.Count == 0) continue;
                grants.Add(new AccessRoleStore.AccessRoleGrant
                {
                    GrantKey = page.Code,
                    Payload = JsonSerializer.Serialize(actions),
                });
            }
        }
        return grants;
    }

    private static List<AccessRoleStore.AccessRoleGrant> BuildDataGrants(IFormCollection form)
    {
        var grants = new List<AccessRoleStore.AccessRoleGrant>();
        foreach (var entity in DataScopeCatalog.Entities)
        {
            var scope = form[$"scope_{entity.Code}"].ToString();
            if (string.IsNullOrWhiteSpace(scope) ||
                scope.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                !DataScopeCatalog.IsValidScope(scope))
            {
                continue;
            }

            grants.Add(new AccessRoleStore.AccessRoleGrant
            {
                GrantKey = entity.Code,
                Payload = JsonSerializer.Serialize(new { scope }),
            });
        }
        return grants;
    }

    private static List<AccessRoleStore.AccessRoleGrant> BuildSensitiveGrants(IFormCollection form) =>
        SensitiveFieldCatalog.Fields
            .Where(field => IsChecked(form, $"sensitive_{field.Code}"))
            .Select(field => new AccessRoleStore.AccessRoleGrant
            {
                GrantKey = field.Code,
            })
            .ToList();

    private static List<AccessRoleStore.AccessRoleGrant> BuildKeyGrants(
        IFormCollection form, string prefix, IEnumerable<string> codes) =>
        codes.Where(code => IsChecked(form, $"{prefix}{code}"))
            .Select(code => new AccessRoleStore.AccessRoleGrant { GrantKey = code })
            .ToList();

    private static bool IsChecked(IFormCollection form, string key) =>
        form[key].Any(value =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));

    private static string DeserializeScope(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return "None";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("scope", out var scope)
                ? scope.GetString() ?? "None"
                : "None";
        }
        catch (JsonException)
        {
            return "None";
        }
    }

    private static List<string> DeserializeActions(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(payload) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
