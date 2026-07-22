using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.AccessRoles;

/// <summary>
/// Access Roles hub (Kayan-parity — docs/kayan-access-roles-study.md). Five tabs,
/// one per role type; each lists reusable roles with their assigned-user counts
/// and create/edit/delete/activate plus user assignment. Admin-only, enforced on
/// every handler. Additive: sits alongside the existing permission system.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public sealed record RoleTypeTab(string Key, string Label);

    public static readonly IReadOnlyList<RoleTypeTab> Tabs = new List<RoleTypeTab>
    {
        new(AccessRoleStore.TypePages, "الصفحات"),
        new(AccessRoleStore.TypeData, "البيانات"),
        new(AccessRoleStore.TypeSensitiveFields, "الحقول الحساسة"),
        new(AccessRoleStore.TypeSelfService, "الخدمة الذاتية"),
        new(AccessRoleStore.TypeReports, "التقارير"),
    };

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = AccessRoleStore.TypePages;

    public string TypeLabel => Tabs.FirstOrDefault(t => t.Key == Type)?.Label ?? Type;

    public List<AccessRoleStore.AccessRole> Roles { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();

    public sealed record UserOption(int Id, string UserName, string? FullName);
    public List<UserOption> Users { get; set; } = new();
    public Dictionary<int, List<int>> AssignedByRole { get; set; } = new();

    public IReadOnlyList<PageCatalog.CatalogModule> Catalog => PageCatalog.Modules;

    /// <summary>Per role: page code → granted actions (for pre-checking the tree on edit).</summary>
    public Dictionary<int, Dictionary<string, List<string>>> GrantsByRole { get; set; } = new();

    private bool IsAdmin =>
        (User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty)
        .Equals("Admin", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        if (!AccessRoleStore.RoleTypes.Contains(Type))
        {
            Type = AccessRoleStore.TypePages;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var form = Request.Form;
        var role = new AccessRoleStore.AccessRole
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            RoleType = Type,
            NameAr = form["NameAr"].ToString().Trim(),
            NameEn = NullIfEmpty(form["NameEn"]),
            Note = NullIfEmpty(form["Note"]),
            IsActive = form["IsActive"] != "false",
        };

        if (string.IsNullOrWhiteSpace(role.NameAr))
        {
            TempData["AccessRoleMessage"] = "اسم الدور مطلوب.";
            return RedirectToPage(new { Type });
        }

        var savedId = await AccessRoleStore.SaveAsync(_dbContext, role);

        var userIds = form["UserIds"]
            .Select(v => int.TryParse(v, out var uid) ? uid : 0)
            .Where(v => v > 0)
            .ToList();
        await AccessRoleStore.ReplaceAssignedUsersAsync(_dbContext, savedId, userIds);

        if (Type == AccessRoleStore.TypePages)
        {
            await AccessRoleStore.ReplaceGrantsAsync(_dbContext, savedId, BuildPageGrants(form));
        }

        TempData["AccessRoleMessage"] = role.Id > 0 ? "تم تحديث الدور." : "تم إنشاء الدور.";
        return RedirectToPage(new { Type });
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        await AccessRoleStore.ToggleActiveAsync(_dbContext, id);
        return RedirectToPage(new { Type });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        await AccessRoleStore.DeleteAsync(_dbContext, id);
        TempData["AccessRoleMessage"] = "تم حذف الدور.";
        return RedirectToPage(new { Type });
    }

    private async Task LoadAsync()
    {
        Roles = await AccessRoleStore.ListAsync(_dbContext, Type);
        Counts = await AccessRoleStore.CountsAsync(_dbContext);

        Users = await _dbContext.SystemUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.UserName)
            .Select(u => new UserOption(u.Id, u.UserName, u.FullName))
            .ToListAsync();

        foreach (var role in Roles)
        {
            AssignedByRole[role.Id] = await AccessRoleStore.GetAssignedUserIdsAsync(_dbContext, role.Id);

            if (Type == AccessRoleStore.TypePages)
            {
                var grants = await AccessRoleStore.GetGrantsAsync(_dbContext, role.Id);
                GrantsByRole[role.Id] = grants.ToDictionary(
                    g => g.GrantKey,
                    g => DeserializeActions(g.Payload),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Reads grant_&lt;PageCode&gt;_&lt;Action&gt; checkboxes into typed grants.</summary>
    private static List<AccessRoleStore.AccessRoleGrant> BuildPageGrants(IFormCollection form)
    {
        var grants = new List<AccessRoleStore.AccessRoleGrant>();

        foreach (var module in PageCatalog.Modules)
        {
            foreach (var page in module.Pages)
            {
                var actions = PageCatalog.Actions
                    .Where(action => form[$"grant_{page.Code}_{action}"] == "on")
                    .ToList();

                if (actions.Count > 0)
                {
                    grants.Add(new AccessRoleStore.AccessRoleGrant
                    {
                        GrantKey = page.Code,
                        Payload = System.Text.Json.JsonSerializer.Serialize(actions),
                    });
                }
            }
        }

        return grants;
    }

    private static List<string> DeserializeActions(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new List<string>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(payload) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
