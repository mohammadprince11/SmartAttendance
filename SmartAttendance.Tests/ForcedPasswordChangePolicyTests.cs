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

    [Fact]
    public void FirstTenantAdministrator_IsDirectedIntoSequentialCompanySetup()
    {
        var root = RepoRoot();
        var passwordModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Account", "ChangePassword.cshtml.cs"));
        var dashboardModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Index.cshtml.cs"));
        var setupPage = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Setup", "Index.cshtml"));
        var companyModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Companies", "Create.cshtml.cs"));

        Assert.Contains("wasForced && !User.IsInRole(\"Employee\")", passwordModel, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(\"/Setup/Index\", new { onboarding = true })", passwordModel, StringComparison.Ordinal);
        Assert.Contains("CompanyOptions.Count == 0", dashboardModel, StringComparison.Ordinal);
        Assert.Contains("nx-setup-wizard__steps", setupPage, StringComparison.Ordinal);
        Assert.Contains("لغات بيانات الشركة", setupPage, StringComparison.Ordinal);
        Assert.Contains("مواقع العمل والفروع", setupPage, StringComparison.Ordinal);
        Assert.Contains("المناصب الوظيفية", setupPage, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(\"/Setup/Index\", new { onboarding = true })", companyModel, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
