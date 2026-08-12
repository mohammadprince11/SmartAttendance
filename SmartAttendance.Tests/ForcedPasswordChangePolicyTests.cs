using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// إجبار تغيير كلمة المرور: حسابٌ موسومٌ يُحوَّل قسرياً إلى صفحة التغيير، إلا على
/// المسارات المعفاة (الصفحة نفسها + الخروج + الدخول) وإلا لدارت الحلقة. وصفحة
/// التغيير مصنّفة AnyAuthenticated فيصلها أيّ دورٍ مصادَق، والوسم لا يغيّر قرار
/// إبطال الجلسة (المستخدم يظلّ صالحاً — يُحوَّل لا يُطرد).
/// </summary>
public class ForcedPasswordChangePolicyTests
{
    [Theory]
    [InlineData("/Account/ChangePassword")]
    [InlineData("/account/changepassword")]
    [InlineData("/account/changepassword/")]
    [InlineData("/Account/Logout")]
    [InlineData("/account/login")]
    public void ExemptPaths_AreNotRedirected(string path) =>
        Assert.True(ForcedPasswordChangePolicy.IsExempt(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/UserAccess")]
    [InlineData("/Employees")]
    [InlineData("/EmployeePortal")]
    [InlineData(null)]
    public void GuardedPaths_AreNotExempt(string? path) =>
        Assert.False(ForcedPasswordChangePolicy.IsExempt(path));

    [Fact]
    public void ChangePasswordPage_IsReachableByAnyAuthenticatedRole() =>
        Assert.Equal(
            PathAccessClass.AnyAuthenticated,
            PublicPathPolicy.Classify("/account/changepassword"));

    [Fact]
    public void MustChangePassword_DoesNotRejectAnOtherwiseValidSession()
    {
        var flagged = new AccountSecurityState(
            Exists: true,
            IsActive: true,
            IsLockedOut: false,
            Role: "Employee",
            SecurityStamp: "stamp-1",
            MustChangePassword: true);

        Assert.Equal(
            SessionSecurityDecision.Valid,
            SessionSecurityValidator.Evaluate("stamp-1", "Employee", flagged));
    }
}
