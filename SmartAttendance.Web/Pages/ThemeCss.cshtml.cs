using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Theming;

namespace SmartAttendance.Web.Pages;

[Authorize]
public sealed class ThemeCssModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IThemeContextService _themeContextService;

    public ThemeCssModel(
        ApplicationDbContext dbContext,
        IThemeContextService themeContextService)
    {
        _dbContext = dbContext;
        _themeContextService = themeContextService;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var theme = await _themeContextService.GetCurrentAsync(cancellationToken);
        var tokens = DesignTokenStore.CompileCss(await DesignTokenStore.LoadAsync(_dbContext));
        var css = string.Concat(tokens, Environment.NewLine, theme.CompiledCss);

        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.ContentType = "text/css; charset=utf-8";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(css, "text/css; charset=utf-8");
    }
}
