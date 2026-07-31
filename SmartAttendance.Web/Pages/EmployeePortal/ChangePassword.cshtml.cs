using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

// تغيير كلمة المرور للخدمة الذاتية للموظف. يعيد استخدام آلية المصادقة القائمة
// (LoginDatabase + SimplePasswordHasher) دون إدخال منطق كلمات مرور جديد.
public class ChangePasswordModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public ChangePasswordModel(ApplicationDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public bool Success { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            ErrorMessage = "انتهت الجلسة. يرجى إعادة تسجيل الدخول.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(CurrentPassword) ||
            string.IsNullOrWhiteSpace(NewPassword) ||
            string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ErrorMessage = "جميع الحقول مطلوبة.";
            return Page();
        }

        if (NewPassword.Length < 8)
        {
            ErrorMessage = "كلمة المرور الجديدة يجب ألا تقل عن 8 محارف.";
            return Page();
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "كلمة المرور الجديدة وتأكيدها غير متطابقين.";
            return Page();
        }

        if (string.Equals(NewPassword, CurrentPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "كلمة المرور الجديدة يجب أن تختلف عن الحالية.";
            return Page();
        }

        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        var user = await LoginDatabase.GetByUsernameAsync(_dbContext, username.Trim());
        if (user == null)
        {
            ErrorMessage = "تعذّر إيجاد الحساب.";
            return Page();
        }

        var currentIsValid = SimplePasswordHasher.Verify(
            CurrentPassword,
            user.PasswordSalt,
            user.PasswordHash);

        if (!currentIsValid)
        {
            ErrorMessage = "كلمة المرور الحالية غير صحيحة.";
            return Page();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await LoginDatabase.UpgradePasswordHashAsync(
            _dbContext,
            user,
            NewPassword,
            ipAddress);

        // المرحلة 5: تغيير كلمة المرور يُبطل كل الجلسات والتوكنات الصادرة سابقاً
        // (أجهزة أخرى/تطبيق الموبايل). الجلسة الحالية تُختم بالختم الجديد فلا
        // يُطرد صاحبها من الصفحة التي غيّر منها.
        var newStamp = await AccountSecurityStore.BumpStampAsync(
            _dbContext,
            _cache,
            user.Id,
            "Password changed by the account owner",
            user.Username);

        if (User.Identity is System.Security.Claims.ClaimsIdentity identity)
        {
            SessionClaimsRefresher.Apply(
                identity,
                new AccountSecurityState(true, true, false, user.Role, newStamp));

            await HttpContext.SignInAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                new System.Security.Claims.ClaimsPrincipal(identity));
        }

        Success = true;
        return Page();
    }
}
