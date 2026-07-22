using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Resolves a user's <see cref="AccessProfile"/> from their assigned Access
/// Roles. Read-only; callers apply Admin bypass above it. This is the
/// enforcement engine — page guards and data-scope filters consume the profile.
/// </summary>
public interface IAccessRoleService
{
    Task<AccessProfile> ResolveAsync(int systemUserId, CancellationToken cancellationToken = default);
}

public sealed class AccessRoleService : IAccessRoleService
{
    private readonly ApplicationDbContext _dbContext;

    public AccessRoleService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AccessProfile> ResolveAsync(int systemUserId, CancellationToken cancellationToken = default)
    {
        if (systemUserId <= 0)
        {
            // Unknown user: unrestricted (no role assigned). Admin bypass is above this.
            return AccessProfile.Build(false, Array.Empty<(string, IEnumerable<string>)>(), Array.Empty<(string, string)>());
        }

        var pagesRoleCount = await AccessRoleStore.CountUserRolesAsync(
            _dbContext, systemUserId, AccessRoleStore.TypePages);

        var pageGrants = pagesRoleCount > 0
            ? await AccessRoleStore.GetUserGrantsAsync(_dbContext, systemUserId, AccessRoleStore.TypePages)
            : new List<AccessRoleStore.AccessRoleGrant>();

        var dataGrants = await AccessRoleStore.GetUserGrantsAsync(
            _dbContext, systemUserId, AccessRoleStore.TypeData);

        return AccessProfile.Build(
            pagesRoleCount > 0,
            pageGrants.Select(g => (g.GrantKey, (IEnumerable<string>)DeserializeActions(g.Payload))),
            dataGrants.Select(g => (g.GrantKey, DeserializeScope(g.Payload))));
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

    private static string DeserializeScope(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "All";
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("scope", out var scope)
                ? scope.GetString() ?? "All"
                : "All";
        }
        catch (System.Text.Json.JsonException)
        {
            return "All";
        }
    }
}
