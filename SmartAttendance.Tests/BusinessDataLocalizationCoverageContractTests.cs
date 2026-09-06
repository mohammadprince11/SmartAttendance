using Xunit;

namespace SmartAttendance.Tests;

public sealed class BusinessDataLocalizationCoverageContractTests
{
    [Fact]
    public void HighVolumePages_UseBusinessDataLocalizationResolver()
    {
        var root = FindRoot();

        AssertContains(
            root,
            "GetEmployeeBusinessDataAsync",
            "SmartAttendance.Web",
            "Pages",
            "Organization",
            "OrgChart.cs");

        AssertContains(
            root,
            "GetEmployeeBusinessDataAsync",
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "OrgStructuresBuilder.cs");

        AssertContains(
            root,
            "GetEmployeeBusinessDataAsync",
            "SmartAttendance.Web",
            "Pages",
            "ShiftAssignments",
            "Index.cshtml.cs");

        AssertContains(
            root,
            "GetEmployeeBusinessDataAsync",
            "SmartAttendance.Web",
            "Pages",
            "EmployeeGeoLocations",
            "Index.cshtml.cs");

        AssertContains(
            root,
            "GetEmployeeBusinessDataAsync",
            "SmartAttendance.Web",
            "Pages",
            "LeaveBalances",
            "Index.cshtml.cs");
    }

    [Fact]
    public void OrganizationSelectorsAndStructureNames_UseLocalizedEntityValues()
    {
        var root = FindRoot();

        AssertContains(
            root,
            "GetCompanyNamesAsync",
            "SmartAttendance.Web",
            "Pages",
            "Organization",
            "Chart.cshtml.cs");

        AssertContains(
            root,
            "GetCompanyNamesAsync",
            "SmartAttendance.Web",
            "Pages",
            "Organization",
            "Index.cshtml.cs");

        AssertContains(
            root,
            "GetEntityNamesAsync",
            "SmartAttendance.Web",
            "Pages",
            "Organization",
            "Index.cshtml.cs");

        AssertContains(
            root,
            "GetCompanyNamesAsync",
            "SmartAttendance.Web",
            "Pages",
            "OrgStructures",
            "Index.cshtml.cs");
    }

    [Fact]
    public void LeaveBalanceSearch_IsAppliedAfterLocalizedProjection()
    {
        var root = FindRoot();

        var source = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "LeaveBalances",
            "Index.cshtml.cs");

        var resolver = source.IndexOf(
            "var localizedEmployeeRows =",
            StringComparison.Ordinal);

        var search = source.IndexOf(
            "employee.FullName.Contains(",
            resolver,
            StringComparison.Ordinal);

        Assert.True(
            resolver >= 0,
            "Localized employee projection was not found.");

        Assert.True(
            search > resolver,
            "Leave balance search must run after localization.");
    }

    private static void AssertContains(
        string root,
        string expected,
        params string[] parts)
    {
        Assert.Contains(
            expected,
            Read(root, parts),
            StringComparison.Ordinal);
    }

    private static string Read(
        string root,
        params string[] parts) =>
        File.ReadAllText(
            Path.Combine(
                new[] { root }
                    .Concat(parts)
                    .ToArray()));

    private static string FindRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}