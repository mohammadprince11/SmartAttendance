namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformAuthenticationDefaults
{
    public const string Scheme = "ZynoraPlatformOwner";
    public const string Policy = "PlatformOwnerOnly";
    public const string OwnerIdClaim = "PlatformOwnerId";
}
