using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Pages.Platform;

[Authorize(Policy = PlatformAuthenticationDefaults.Policy)]
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Platform/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(PlatformAuthenticationDefaults.Scheme);
        return RedirectToPage("/Platform/Login");
    }
}
