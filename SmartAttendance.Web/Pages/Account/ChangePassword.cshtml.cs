using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Account;

/// <summary>
/// تغيير كلمة المرور العامّ لكل الأدوار (باك أوفيس وموظف). يخدم مساري:
/// (1) الطوعيّ من قائمة الحساب. (2) القسريّ حين يُوسَم الحساب بـ
/// <c>MustChangePassword</c> (إعادة تعيينٍ جماعيّة من /UserAccess) — عندئذٍ
/// يحوّله الحارس هنا ولا يمرّره حتى يعيّن كلمةً جديدة. النجاح يصفّر الوسم.
/// يعيد استخدام آلية المصادقة القائمة دون منطق كلمات مرورٍ جديد.
/// </summary>
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

    /// <summary>وضع قسريّ — يُعرَض تنبيهٌ ويُخفى زر الرجوع.</summary>
    public bool IsForced { get; set; }

    /// <summary>وجهة المتابعة بعد النجاح حسب الدور (الموظف لبوابته لا لِـ«/»).</summary>
    public string HomePath =>
        User.IsInRole("Employee") ? "/EmployeePortal" : "/";

    public async Task OnGetAsync()
    {
        IsForced = await ReadMustChangeAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            ErrorMessage = "انتهت الجلسة. يرجى إعادة تسجيل الدخول.";
            return Page();
        }

        IsForced = await ReadMustChangeAsync();

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

        // تصفير وسم الإجبار داخل نفس التدفّق: من هنا فصاعداً يعبر الحارس بلا تحويل.
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE AppLoginUsers
SET MustChangePassword = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", user.Id));

        // تغيير كلمة المرور يُبطل كل الجلسات والتوكنات الصادرة سابقاً (أجهزة أخرى).
        // الجلسة الحالية تُختم بالختم الجديد فلا يُطرد صاحبها من صفحة التغيير.
        var newStamp = await AccountSecurityStore.BumpStampAsync(
            _dbContext,
            _cache,
            user.Id,
            "Password changed by the account owner",
            user.Username);

        if (User.Identity is ClaimsIdentity identity)
        {
            SessionClaimsRefresher.Apply(
                identity,
                new AccountSecurityState(true, true, false, user.Role, newStamp));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new System.Security.Claims.ClaimsPrincipal(identity));
        }

        IsForced = false;
        Success = true;
        return Page();
    }

    private async Task<bool> ReadMustChangeAsync()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var state = await AccountSecurityStore.GetStateAsync(
            _dbContext,
            _cache,
            username);

        return state.Exists && state.MustChangePassword;
    }
}
