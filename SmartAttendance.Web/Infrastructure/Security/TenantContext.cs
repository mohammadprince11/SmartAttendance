using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class TenantContext
{
    public const string TenantIdClaimType = "TenantId";
    public const string TenantCodeClaimType = "TenantCode";

    public static int? GetTenantId(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(TenantIdClaimType)?.Value;
        return int.TryParse(raw, out var tenantId) && tenantId > 0
            ? tenantId
            : null;
    }

    public static string? GetTenantCode(ClaimsPrincipal? principal) =>
        principal?.FindFirst(TenantCodeClaimType)?.Value;

    public static bool IsValidCode(string? code) =>
        code is { Length: 4 } && code.All(char.IsAsciiDigit);

    public static async Task<TenantIdentity?> ResolveAsync(
        ApplicationDbContext dbContext,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var normalized = code?.Trim();
        if (!IsValidCode(normalized))
        {
            return null;
        }

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .Where(x => x.Code == normalized && x.IsActive)
            .Select(x => new TenantIdentity(x.Id, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant is null || !await PlatformPortalStore.IsTenantAccessAllowedAsync(
                dbContext,
                tenant.Id,
                DateTime.UtcNow))
        {
            return null;
        }

        return tenant;
    }

    public sealed record TenantIdentity(int Id, string Code, string Name);
}
