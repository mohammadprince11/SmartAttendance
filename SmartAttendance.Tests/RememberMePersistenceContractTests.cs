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
