using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Culture;

[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
[AllowAnonymous]
public sealed class CatalogModel : PageModel
{
    private readonly ILocalizationDictionaryService _dictionary;

    public CatalogModel(
        ILocalizationDictionaryService dictionary)
    {
        _dictionary = dictionary;
    }

    public async Task<IActionResult> OnGetAsync(
        string? culture)
    {
        var languages =
            await _dictionary.GetLanguagesAsync(
                HttpContext.RequestAborted);

        var requested =
            languages.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    culture,
                    StringComparison.OrdinalIgnoreCase))
            ??
            languages.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    CultureInfo.CurrentUICulture.Name,
                    StringComparison.OrdinalIgnoreCase))
            ??
            languages.FirstOrDefault();

        if (requested is null)
            return NotFound();

        var translations =
            await _dictionary.GetCatalogAsync(
                requested.Code,
                HttpContext.RequestAborted);

        return new JsonResult(new
        {
            culture = requested.Code,
            direction = requested.Direction,
            translations
        });
    }
}