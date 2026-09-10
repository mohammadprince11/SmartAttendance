using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class OnlinePunchDuplicateCleanupContractTests
{
    [Fact]
    public void EmployeeOnlinePunches_PhysicalRazorPage_IsDeleted()
    {
        Assert.False(File.Exists(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            "EmployeeOnlinePunches",
            "Index.cshtml")));
    }

    [Fact]
    public void OnlinePunches_IsNotASeparateVisiblePageGrant() =>
        Assert.False(PageCatalog.IsValidPage("Attendance.OnlinePunches"));

    [Fact]
    public void LegacyOnlinePunchUrl_UsesAttendanceRecordsPermission() =>
        Assert.Equal(
            "Attendance.Records",
            PageAccessRouteCatalog.ResolvePageCode("/EmployeeOnlinePunches"));

    [Fact]
    public void LegacyStoredOnlinePunchGrant_FoldsIntoAttendanceRecords()
    {
        var profile = AccessProfile.Build(
            hasPagesRole: true,
            pageGrants: new[]
            {
                ("Attendance.OnlinePunches",
                    (IEnumerable<string>)new[] { "View", "Delete" }),
            },
            dataGrants: Array.Empty<(string Key, string Scope)>());

        Assert.True(profile.Can("Attendance.Records", "View"));
        Assert.True(profile.Can("Attendance.Records", "Delete"));
        Assert.False(profile.PageActions.ContainsKey("Attendance.OnlinePunches"));
    }

    [Fact]
    public void Program_PreservesLegacyOnlinePunchUrl()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Program.cs"));

        Assert.Contains(
            "app.MapGet(\"/EmployeeOnlinePunches\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AttendanceRecords?Source=Mobile",
            source,
            StringComparison.Ordinal);
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