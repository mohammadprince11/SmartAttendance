namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Canonical values accepted by CK_SelfServiceRequests_RequestSource.
/// Keep request-origin semantics in one place instead of repeating SQL string literals.
/// </summary>
public static class RequestSourceCatalog
{
    public const string SelfService = "SelfService";
    public const string Admin = "Admin";
    public const string Legacy = "Legacy";

    public static bool IsValid(string? value) =>
        value is SelfService or Admin or Legacy;

    public static string Normalize(string? value) =>
        IsValid(value) ? value! : Legacy;
}
