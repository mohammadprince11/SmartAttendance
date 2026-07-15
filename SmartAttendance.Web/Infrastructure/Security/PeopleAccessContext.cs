using System.Security.Claims;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class PeopleAccessContext
{
    private const string SystemUserIdItemKey = "NEXORA.PeopleAccess.SystemUserId";

    public static void SetSystemUserId(HttpContext context, int? systemUserId)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (systemUserId.HasValue && systemUserId.Value > 0)
        {
            context.Items[SystemUserIdItemKey] = systemUserId.Value;
        }
    }

    public static int? GetSystemUserId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(SystemUserIdItemKey, out var value) &&
            value is int itemValue &&
            itemValue > 0)
        {
            return itemValue;
        }

        var claimValue = context.User.FindFirstValue("SystemUserId");

        return int.TryParse(claimValue, out var claimId) && claimId > 0
            ? claimId
            : null;
    }

    public static string GetRole(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.User.FindFirstValue(ClaimTypes.Role)?.Trim() ?? string.Empty;
    }
}
