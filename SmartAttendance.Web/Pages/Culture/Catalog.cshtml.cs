using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Web;

namespace SmartAttendance.Web.Pages.Culture;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AllowAnonymous]
public sealed class CatalogModel : PageModel
{
    public IActionResult OnGet()
    {
        // Load only the exact satellite catalog. There is intentionally no neutral
        // .resx: Arabic source strings are their own fallback keys.
        var manager = new ResourceManager(
            "SmartAttendance.Web.Resources.SharedResource",
            typeof(SharedResource).Assembly);
        var resourceSet = manager.GetResourceSet(
            CultureInfo.CurrentUICulture,
            createIfNotExists: true,
            tryParents: false);
        var translations = resourceSet?
            .Cast<DictionaryEntry>()
            .Where(item => item.Key is string && item.Value is string)
            .ToDictionary(
                item => (string)item.Key,
                item => (string)item.Value!,
                StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new JsonResult(new
        {
            culture = System.Globalization.CultureInfo.CurrentUICulture.Name,
            direction = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "rtl" : "ltr",
            translations
        });
    }
}
