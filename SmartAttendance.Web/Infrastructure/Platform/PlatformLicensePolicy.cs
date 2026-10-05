namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformLicensePolicy
{
    public static readonly string[] AllowedStatuses =
        ["Trial", "Active", "Suspended", "Expired", "Cancelled"];

    public static bool AllowsAccess(
        string? status,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc,
        DateTime? graceEndsAtUtc,
        DateTime utcNow)
    {
        if (status is not ("Trial" or "Active") || utcNow < startsAtUtc)
        {
            return false;
        }

        if (!expiresAtUtc.HasValue || utcNow <= expiresAtUtc.Value)
        {
            return true;
        }

        return graceEndsAtUtc.HasValue && utcNow <= graceEndsAtUtc.Value;
    }

    public static string DisplayStatus(
        string status,
        DateTime? expiresAtUtc,
        DateTime? graceEndsAtUtc,
        DateTime utcNow)
    {
        if (status == "Suspended") return "موقوفة";
        if (status == "Cancelled") return "ملغاة";
        if (status == "Expired") return "منتهية";
        if (expiresAtUtc.HasValue && utcNow > expiresAtUtc.Value)
        {
            return graceEndsAtUtc.HasValue && utcNow <= graceEndsAtUtc.Value
                ? "فترة سماح"
                : "منتهية";
        }

        return status == "Trial" ? "تجريبية" : "فعّالة";
    }
}
