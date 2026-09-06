using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeProfileBusinessDataUiCultureContractTests
{
    [Fact]
    public void EmployeeProfile_UsesUiCultureForBusinessData()
    {
        var root = FindRoot();

        var path = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Profile.cshtml.cs");

        var source = File.ReadAllText(path);

        Assert.Contains(
            "CultureInfo.CurrentUICulture.Name",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "await LocalizeEmployeeProfileAsync(employee);",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "item.EntityType == \"Employee\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"Company\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"Branch\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"Department\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"Position\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "DirectManagerId",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory =
            new DirectoryInfo(Directory.GetCurrentDirectory());

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