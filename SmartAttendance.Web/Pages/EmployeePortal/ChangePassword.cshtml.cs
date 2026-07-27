using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

// تغيير كلمة المرور للخدمة الذاتية للموظف. يعيد استخدام آلية المصادقة القائمة
// (LoginDatabase + SimplePasswordHasher) دون إدخال منطق كلمات مرور جديد.
public class ChangePasswordModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ChangePasswordModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        Success = true;
        return Page();
    }
}
