namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Resolves the active <see cref="ThemeContext"/> for the current request,
/// backed by an in-memory cache keyed by company and version. Implementations
/// must never throw for the caller: any failure resolves to
/// <see cref="ThemeContext.Default"/> so the shell always renders.
/// </summary>
public interface IThemeContextService
{
    Task<ThemeContext> GetCurrentAsync(CancellationToken cancellationToken = default);
}
