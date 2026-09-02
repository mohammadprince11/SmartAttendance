namespace ZynoraHR.Mobile;

public static class TrustedNavigationPolicy
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "zynorahr.com",
        "www.zynorahr.com"
    };

    public static bool IsAllowed(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        AllowedHosts.Contains(uri.Host);

    public static bool CanOpenExternally(Uri uri) =>
        uri.Scheme is "https" or "http" or "mailto" or "tel";
}
