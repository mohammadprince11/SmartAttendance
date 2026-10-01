namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformHostPolicy
{
    public const string ConfigurationKey = "PlatformPortal:AllowedHosts";

    public static bool IsPlatformPath(PathString path) =>
        path.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedHost(
        HostString requestHost,
        IConfiguration configuration)
    {
        var allowedHosts = configuration
            .GetSection(ConfigurationKey)
            .Get<string[]>()?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalizedRequestHost = Normalize(requestHost.Host);
        if (allowedHosts.Count > 0)
        {
            return allowedHosts.Contains(normalizedRequestHost);
        }

        // Development-safe default: the owner portal is visible only on loopback
        // until a real, separate hostname is explicitly configured.
        return normalizedRequestHost is "localhost" or "127.0.0.1" or "::1";
    }

    private static string Normalize(string? host) =>
        (host ?? string.Empty).Trim().TrimEnd('.');
}

public sealed class PlatformHostMiddleware
{
    private readonly RequestDelegate _next;

    public PlatformHostMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (PlatformHostPolicy.IsPlatformPath(context.Request.Path) &&
            !PlatformHostPolicy.IsAllowedHost(context.Request.Host, configuration))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}
