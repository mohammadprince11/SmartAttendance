using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Account;

public class LoginModel : PageModel
{
    private const string GenericLoginError =
        "بيانات الدخول غير صحيحة أو الحساب غير متاح مؤقتاً.";

    private static readonly TimeSpan StandardSessionDuration =
        TimeSpan.FromHours(8);

    private static readonly TimeSpan RememberedSessionDuration =
        TimeSpan.FromDays(30);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILoginIdentityService _loginIdentityService;

    public LoginModel(
        ApplicationDbContext dbContext,
        ILoginIdentityService loginIdentityService)
    {
        _dbContext = dbContext;
        _loginIdentityService = loginIdentityService;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        ApplyNoStoreHeaders();
        await LoginDatabase.EnsureCreatedAsync(_dbContext);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ApplyNoStoreHeaders();
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "اسم المستخدم وكلمة المرور مطلوبة.";
            return Page();
        }

        var normalizedUsername = Username.Trim();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var utcNow = DateTime.UtcNow;
        var user = await LoginDatabase.GetByUsernameAsync(
            _dbContext,
            normalizedUsername);

        if (user == null)
        {
            SimplePasswordHasher.PerformDummyVerification(Password);

            await LoginDatabase.RecordUnknownLoginFailureAsync(
                _dbContext,
                normalizedUsername,
                ipAddress);

            ErrorMessage = GenericLoginError;
            return Page();
        }

        if (!user.IsActive)
        {
            SimplePasswordHasher.PerformDummyVerification(Password);

            await LoginDatabase.RecordRejectedLoginAsync(
                _dbContext,
                user,
                "Inactive login identity",
                ipAddress);

            ErrorMessage = GenericLoginError;
            return Page();
        }

        if (user.IsLockedOut(utcNow))
        {
            SimplePasswordHasher.PerformDummyVerification(Password);

            await LoginDatabase.RecordRejectedLoginAsync(
                _dbContext,
                user,
                "Login identity is temporarily locked",
                ipAddress);

            ErrorMessage = GenericLoginError;
            return Page();
        }

        var passwordIsValid = SimplePasswordHasher.Verify(
            Password,
            user.PasswordSalt,
            user.PasswordHash);

        if (!passwordIsValid)
        {
            await LoginDatabase.RecordFailedLoginAsync(
                _dbContext,
                user,
                ipAddress,
                utcNow);

            ErrorMessage = GenericLoginError;
            return Page();
        }

        if (SimplePasswordHasher.NeedsRehash(user.PasswordHash))
        {
            await LoginDatabase.UpgradePasswordHashAsync(
                _dbContext,
                user,
                Password,
                ipAddress);
        }

        var displayName = !string.IsNullOrWhiteSpace(user.EmployeeName)
            ? user.EmployeeName
            : user.Username;

        int? systemUserId;

        try
        {
            systemUserId = await _loginIdentityService.EnsureSystemUserAsync(
                new LoginIdentityRequest
                {
                    EmployeeId = user.EmployeeId,
                    UserName = user.Username,
                    DisplayName = displayName,
                    CompatibilityRole = user.Role,
                    IsActive = user.IsActive
                },
                HttpContext.RequestAborted);
        }
        catch
        {
            await LoginDatabase.RecordRejectedLoginAsync(
                _dbContext,
                user,
                "System identity synchronization failed",
                ipAddress);

            ErrorMessage =
                "تعذر إكمال تسجيل الدخول حالياً. يرجى المحاولة مرة أخرى.";
            return Page();
        }

        if (!systemUserId.HasValue || systemUserId.Value <= 0)
        {
            await LoginDatabase.RecordRejectedLoginAsync(
                _dbContext,
                user,
                "System identity synchronization returned no identity",
                ipAddress);

            ErrorMessage =
                "تعذر إكمال تسجيل الدخول حالياً. يرجى المحاولة مرة أخرى.";
            return Page();
        }

        var issuedUtc = DateTimeOffset.UtcNow;
        var sessionDuration = RememberMe
            ? RememberedSessionDuration
            : StandardSessionDuration;
        var expiry = issuedUtc.Add(sessionDuration);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("DisplayName", displayName),
            new("EmployeeId", user.EmployeeId?.ToString() ?? string.Empty),
            new("SystemUserId", systemUserId.Value.ToString()),
            new("SessionIssuedUtc", issuedUtc.ToString("O"))
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = RememberMe,
            IssuedUtc = issuedUtc,
            ExpiresUtc = expiry,
            AllowRefresh = true
        };

        await LoginDatabase.RecordSuccessfulLoginAsync(
            _dbContext,
            user,
            ipAddress);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);

        DeleteLegacyIdentityCookies();

        if (!string.IsNullOrWhiteSpace(ReturnUrl) &&
            Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        if (user.Role.Equals(
                "Employee",
                StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/EmployeePortal/Index");
        }

        return RedirectToPage("/Index");
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
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
