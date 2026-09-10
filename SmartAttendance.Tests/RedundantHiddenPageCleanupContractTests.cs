using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class RedundantHiddenPageCleanupContractTests
{
    [Theory]
    [InlineData("AttendanceProcessing")]
    [InlineData("AttendanceCorrections")]
    [InlineData("AttendanceImports")]
    public void HiddenAttendanceRedirectRazorPages_AreDeleted(string folder)
    {
        Assert.False(File.Exists(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            folder,
            "Index.cshtml")));
    }

    [Theory]
    [InlineData("Attendance.Imports")]
    [InlineData("Attendance.Processing")]
    [InlineData("Attendance.Corrections")]
    [InlineData("Payroll.TaxSocial")]
    public void DuplicateOrGhostPageCodes_AreRemoved(string code) =>
        Assert.False(PageCatalog.IsValidPage(code));

    [Theory]
    [InlineData("/AttendanceImports")]
    [InlineData("/AttendanceProcessing")]
    [InlineData("/AttendanceCorrections")]
    public void LegacyAttendanceUrls_ResolveToCanonicalPermission(string path) =>
        Assert.Equal(
            "Attendance.Operations",
            PageAccessRouteCatalog.ResolvePageCode(path));

    [Fact]
    public void LegacyStoredPageGrant_IsFoldedIntoCanonicalOperationsGrant()
    {
        var profile = AccessProfile.Build(
            hasPagesRole: true,
            pageGrants: new[]
            {
                ("Attendance.Imports", (IEnumerable<string>)new[] { "View", "Create" }),
            },
            dataGrants: Array.Empty<(string Key, string Scope)>());

        Assert.True(profile.Can("Attendance.Operations", "View"));
        Assert.True(profile.Can("Attendance.Operations", "Create"));
        Assert.False(profile.PageActions.ContainsKey("Attendance.Imports"));
    }

    [Fact]
    public void Program_PreservesLegacyUrlsAsRedirects()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Program.cs"));

        Assert.Contains("app.MapGet(\"/AttendanceProcessing\"", source, StringComparison.Ordinal);
        Assert.Contains("AttendanceOperations?Tab=process", source, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/AttendanceCorrections\"", source, StringComparison.Ordinal);
        Assert.Contains("AttendanceOperations?Tab=corrections", source, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/AttendanceImports\"", source, StringComparison.Ordinal);
        Assert.Contains("AttendanceOperations?Tab=import", source, StringComparison.Ordinal);
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