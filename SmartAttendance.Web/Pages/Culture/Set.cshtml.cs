using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Web;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Culture;

[AllowAnonymous]
public sealed class SetModel : PageModel
{
    private readonly ILocalizationDictionaryService _dictionary;

    public SetModel(ILocalizationDictionaryService dictionary) => _dictionary = dictionary;

    public async Task<IActionResult> OnPostAsync(string? culture, string? returnUrl)
    {
        var supported = await _dictionary.FindLanguageAsync(culture, HttpContext.RequestAborted);
        if (supported is null)
            return BadRequest();

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supported.Code)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/"
            });

        var destination = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        return LocalRedirect(destination);
    }
}
