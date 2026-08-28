using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Web;

namespace SmartAttendance.Web.Pages.Culture;

[AllowAnonymous]
public sealed class SetModel : PageModel
{
    public IActionResult OnPost(string? culture, string? returnUrl)
    {
        if (!ZynoraSupportedCultures.TryGet(culture, out var supported))
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
