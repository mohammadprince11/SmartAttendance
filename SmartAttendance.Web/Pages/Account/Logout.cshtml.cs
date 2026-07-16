using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public LogoutModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var loginUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = User.Identity?.Name;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await LoginDatabase.RecordLogoutAsync(
            _dbContext,
            loginUserId,
            username,
            ipAddress);

        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        DeleteLegacyIdentityCookies();

        return RedirectToPage("/Account/Login");
    }

    private void DeleteLegacyIdentityCookies()
    {
        Response.Cookies.Delete("NEXORA.Auth");
        Response.Cookies.Delete("SA.UserId");
        Response.Cookies.Delete("SA.UserName");
        Response.Cookies.Delete("SA.DisplayName");
        Response.Cookies.Delete("SA.Role");
        Response.Cookies.Delete("SA.EmployeeId");
    }
}
