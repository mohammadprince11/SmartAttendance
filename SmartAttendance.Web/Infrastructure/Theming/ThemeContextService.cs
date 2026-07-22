using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Runtime theme resolver (Phase P4). Resolves the current company from the
/// selection cookie and returns its cached <see cref="ThemeContext"/>. Until
/// persistence lands (P5) no company has a published theme, so this always
/// yields the ZYNORA Default while exercising the full cache path
/// (<c>CompanyTheme:{companyId}:{version}</c>). Any failure falls back to the
/// default so a broken theme can never take down the shell.
/// </summary>
public sealed class ThemeContextService : IThemeContextService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ThemeContextService> _logger;

    public ThemeContextService(
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ThemeContextService> logger)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public Task<ThemeContext> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var companyId = ResolveCompanyId();
            if (companyId is null)
            {
                return Task.FromResult(ThemeContext.Default);
            }

            // P5 will read the company's active published version here; for now the
            // default version is the only one that exists.
            var version = ThemeContext.DefaultVersion;
            var cacheKey = BuildCacheKey(companyId.Value, version);

            var context = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;

                // P5: load CompiledCss from the persisted published ThemeVersion.
                // Empty CompiledCss keeps the ZYNORA Default rendering.
                return new ThemeContext
                {
                    CompanyId = companyId,
                    Version = version,
                };
            })!;

            return Task.FromResult(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Theme resolution failed; falling back to the ZYNORA Default.");
            return Task.FromResult(ThemeContext.Default);
        }
    }

    /// <summary>Cache key shared with the compiler/invalidation path in later phases.</summary>
    public static string BuildCacheKey(int companyId, string version) =>
        $"CompanyTheme:{companyId}:{version}";

    private int? ResolveCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Request.Cookies.TryGetValue(
                CompanySelectionContext.CookieName,
                out var raw) &&
            int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var companyId) &&
            companyId > 0)
        {
            return companyId;
        }

        return null;
    }
}
