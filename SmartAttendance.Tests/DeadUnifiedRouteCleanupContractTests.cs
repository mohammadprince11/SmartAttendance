using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class DeadUnifiedRouteCleanupContractTests
{
    [Theory]
    [InlineData("Engagement", "Recognition.cshtml")]
    [InlineData("Engagement", "Recognition.cshtml.cs")]
    [InlineData("Violations", "Actions.cshtml")]
    [InlineData("Violations", "Actions.cshtml.cs")]
    public void DeadPhysicalPages_AreDeleted(string folder, string fileName)
    {
        Assert.False(File.Exists(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            folder,
            fileName)));
    }

    [Fact]
    public void UnifiedPages_ContainMigratedFunctionality()
    {
        var root = FindRoot();

        var engagement = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Engagement",
            "Index.cshtml"));

        Assert.Contains(
            "data-zyw-tab=\"recognition\"",
            engagement,
            StringComparison.Ordinal);

        Assert.Contains(
            "RecognitionAnnouncements",
            engagement,
            StringComparison.Ordinal);

        Assert.Contains(
            "CampaignAnnouncements",
            engagement,
            StringComparison.Ordinal);

        var violations = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Violations",
            "Index.cshtml"));

        Assert.Contains(
            "data-zyw-tab=\"actions\"",
            violations,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalUrls_UseCanonicalModulePermissions()
    {
        Assert.Equal(
            "People.Engagement",
            PageAccessRouteCatalog.ResolvePageCode("/Engagement/Recognition"));

        Assert.Equal(
            "People.Violations",
            PageAccessRouteCatalog.ResolvePageCode("/Violations/Actions"));
    }

    [Fact]
    public void Program_PreservesHistoricalUrlsAsRedirects()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Program.cs"));

        Assert.Contains(
            "app.MapGet(\"/Engagement/Recognition\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "/Engagement?tab=recognition",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "app.MapGet(\"/Violations/Actions\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "/Violations?tab=actions",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EngagementNavigation_UsesCanonicalTab()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            "Engagement",
            "_EngagementNav.cshtml"));

        Assert.Contains(
            "asp-page=\"/Engagement/Index\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "asp-route-tab=\"recognition\"",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "asp-page=\"/Engagement/Recognition\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanViolationActionsCss_IsDeleted()
    {
        Assert.False(File.Exists(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "wwwroot",
            "css",
            "pages",
            "actions-93fb669231.css")));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}