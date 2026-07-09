using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Account;

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        Response.Cookies.Delete("SA.UserId");
        Response.Cookies.Delete("SA.UserName");
        Response.Cookies.Delete("SA.DisplayName");
        Response.Cookies.Delete("SA.Role");
        Response.Cookies.Delete("SA.EmployeeId");

        return RedirectToPage("/Account/Login");
    }
}
