using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Web;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Culture;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AllowAnonymous]
public sealed class CatalogModel : PageModel
{
    private readonly ILocalizationDictionaryService _dictionary;

    public CatalogModel(ILocalizationDictionaryService dictionary) => _dictionary = dictionary;

    public async Task<IActionResult> OnGetAsync(string? culture)
    {
        var requested = await _dictionary.FindLanguageAsync(culture, HttpContext.RequestAborted)
            ?? await _dictionary.FindLanguageAsync(CultureInfo.CurrentUICulture.Name, HttpContext.RequestAborted)
            ?? await _dictionary.FindLanguageAsync(ZynoraSupportedCultures.DefaultCode, HttpContext.RequestAborted);
        if (requested is null) return NotFound();
        var translations = await _dictionary.GetCatalogAsync(requested.Code, HttpContext.RequestAborted);

        return new JsonResult(new
        {
            culture = requested.Code,
            direction = requested.Direction,
            translations
        });
    }
}
