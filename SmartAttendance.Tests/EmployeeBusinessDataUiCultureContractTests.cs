using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeBusinessDataUiCultureContractTests
{
    [Fact]
    public void EmployeePages_UseBusinessDataDisplayLocalization()
    {
        var root = FindRoot();

        var helper = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Localization",
            "EmployeeBusinessDataDisplayLocalizer.cs"));

        var index = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Index.cshtml.cs"));

        var edit = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Edit.cshtml.cs"));

        var create = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Create.cshtml.cs"));

        var service = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Infrastructure",
            "Services",
            "EmployeeService.cs"));

        Assert.Contains(
            "CultureInfo.CurrentUICulture.Name",
            helper,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeEmployeeListAsync",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeBusinessLookupsAsync",
            edit,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeBusinessLookupsAsync",
            create,
            StringComparison.Ordinal);

        Assert.Contains(
            "PositionId = x.PositionId",
            service,
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