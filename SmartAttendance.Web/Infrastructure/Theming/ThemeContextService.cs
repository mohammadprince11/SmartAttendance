using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Runtime theme resolver. Resolves the current company from the selection
/// cookie and returns its published <see cref="ThemeContext"/> from an in-memory
/// cache keyed by company; a miss loads the active published version from
/// <see cref="ThemeStore"/>. Companies without a published theme (and any
/// failure) resolve to <see cref="ThemeContext.Default"/> so a broken or absent
/// theme can never take down the shell.
/// </summary>
public sealed class ThemeContextService : IThemeContextService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ThemeContextService> _logger;

    public ThemeContextService(
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext dbContext,
        ILogger<ThemeContextService> logger)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ThemeContext> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var companyId = ResolveCompanyId();
            if (companyId is null)
            {
                return ThemeContext.Default;
            }

            var cacheKey = CompanyCacheKey(companyId.Value);

            var context = await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;

                var published = await ThemeStore.GetActivePublishedAsync(_dbContext, companyId.Value);
                if (published is null)
                {
                    // Company has no published theme yet: ZYNORA Default.
                    return new ThemeContext { CompanyId = companyId };
                }

                return new ThemeContext
                {
                    CompanyId = companyId,
                    Version = published.VersionId.ToString(CultureInfo.InvariantCulture),
                    CompiledCss = published.CompiledCss,
                };
            });

            return context ?? ThemeContext.Default;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Theme resolution failed; falling back to the ZYNORA Default.");
            return ThemeContext.Default;
        }
    }

    /// <summary>
    /// Drops a company's cached theme so the next request recompiles from the
    /// store. Call after publish or rollback. IMemoryCache is a singleton, so
    /// this reaches every scoped resolver.
    /// </summary>
    public void Invalidate(int companyId) =>
        _cache.Remove(CompanyCacheKey(companyId));

    /// <summary>Runtime cache key for a company's resolved theme.</summary>
    public static string CompanyCacheKey(int companyId) =>
        $"CompanyTheme:{companyId}";

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
