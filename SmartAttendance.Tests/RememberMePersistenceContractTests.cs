using Xunit;

namespace SmartAttendance.Tests;

public sealed class RememberMePersistenceContractTests
{
    [Fact]
    public void Login_IssuesPersistentThirtyDayCookieAndSignedRememberedClaim()
    {
        var login = ReadWeb("Pages/Account/Login.cshtml.cs");

        Assert.Contains("RememberedSessionDuration", login, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromDays(30)", login, StringComparison.Ordinal);
        Assert.Contains("IsPersistent = RememberMe", login, StringComparison.Ordinal);
        Assert.Contains("PortalSessionPolicy.RememberMeClaimType", login, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_OnlySkipsLogoutTimersForRememberedSession()
    {
        var layout = ReadWeb("Pages/Shared/_EmployeePortalLayout.cshtml")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("@if (!embed)\n    {\n        <div class=\"nxex-embed-backdrop\"", layout, StringComparison.Ordinal);
        Assert.Contains("@if (!embed && !__zyRememberedSession)\n    {\n        var __zySessionDb", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_DoesNotDeleteTheActiveAuthenticationCookieAfterSignIn()
    {
        var program = ReadWeb("Program.cs");
        var login = ReadWeb("Pages/Account/Login.cshtml.cs");

        Assert.Contains("options.Cookie.Name = \"ZYNORA.Auth\";", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Response.Cookies.Delete(\"ZYNORA.Auth\")", login, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginCard_KeepsBalancedSpaceInsideTheVisualStage()
    {
        var css = ReadWeb("wwwroot/css/login-foundation.css");

        Assert.Contains(".login-stage > .login-card", css, StringComparison.Ordinal);
        Assert.Contains("width: calc(100% - 32px) !important;", css, StringComparison.Ordinal);
        Assert.Contains("margin: 16px !important;", css, StringComparison.Ordinal);
        Assert.Contains("width: calc(100% - 24px) !important;", css, StringComparison.Ordinal);
        Assert.Contains("margin: 12px !important;", css, StringComparison.Ordinal);
    }

    private static string ReadWeb(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;

        var root = Assert.IsType<DirectoryInfo>(directory).FullName;
        return File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
