using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Platform;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public LoginModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        ApplyNoStore();
        if (User.Identities.Any(identity =>
                identity.AuthenticationType == PlatformAuthenticationDefaults.Scheme && identity.IsAuthenticated))
        {
            return RedirectToPage("/Platform/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ApplyNoStore();
        var normalizedUsername = Username?.Trim() ?? string.Empty;
        if (normalizedUsername.Length is < 3 or > 100 || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "بيانات الدخول غير صحيحة.";
            return Page();
        }

        var owner = await PlatformPortalStore.FindOwnerAsync(_db, normalizedUsername);
        if (owner is null || !owner.IsActive ||
            !SimplePasswordHasher.Verify(Password, owner.PasswordSalt, owner.PasswordHash))
        {
            SimplePasswordHasher.PerformDummyVerification(Password);
            ErrorMessage = "بيانات الدخول غير صحيحة.";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()),
            new Claim(ClaimTypes.Name, owner.Username),
            new Claim("DisplayName", owner.DisplayName),
            new Claim(PlatformAuthenticationDefaults.OwnerIdClaim, owner.Id.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            PlatformAuthenticationDefaults.Scheme));

        await HttpContext.SignInAsync(
            PlatformAuthenticationDefaults.Scheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4),
                AllowRefresh = false
            });

        await PlatformPortalStore.RecordOwnerLoginAsync(
            _db,
            owner.Id,
            owner.Username,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("/Platform/Index");
    }

    private void ApplyNoStore()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
