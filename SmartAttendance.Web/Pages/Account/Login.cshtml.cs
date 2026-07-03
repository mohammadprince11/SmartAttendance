using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public LoginModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
        await LoginDatabase.EnsureCreatedAsync(_dbContext);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "اسم المستخدم وكلمة المرور مطلوبة.";
            return Page();
        }

        var user = await LoginDatabase.GetByUsernameAsync(_dbContext, Username.Trim());

        if (user == null || !user.IsActive)
        {
            ErrorMessage = "بيانات الدخول غير صحيحة أو المستخدم غير فعال.";
            return Page();
        }

        var ok = false;

        try
        {
            ok = SimplePasswordHasher.Verify(Password, user.PasswordSalt, user.PasswordHash);
        }
        catch
        {
            ok = false;
        }

        if (!ok)
        {
            ErrorMessage = "بيانات الدخول غير صحيحة.";
            return Page();
        }

        var days = RememberMe ? 30 : 1;
        var expiry = DateTimeOffset.UtcNow.AddDays(days);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = expiry,
            MaxAge = TimeSpan.FromDays(days),
            IsEssential = true,
            Path = "/"
        };

        var displayName = !string.IsNullOrWhiteSpace(user.EmployeeName)
            ? user.EmployeeName
            : user.Username;

        Response.Cookies.Append("SA.UserId", user.Id.ToString(), cookieOptions);
        Response.Cookies.Append("SA.UserName", user.Username, cookieOptions);
        Response.Cookies.Append("SA.DisplayName", displayName, cookieOptions);
        Response.Cookies.Append("SA.Role", user.Role, cookieOptions);
        Response.Cookies.Append("SA.EmployeeId", user.EmployeeId?.ToString() ?? "", cookieOptions);

        await LoginDatabase.UpdateLastLoginAsync(_dbContext, user.Id);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && ReturnUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return Redirect(ReturnUrl);
        }

        return RedirectToPage("/Index");
    }
}
