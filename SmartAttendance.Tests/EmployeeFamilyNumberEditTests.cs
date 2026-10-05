using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeFamilyNumberEditTests
{
    [Fact]
    public void EmployeeEdit_UsesIdentityDocumentFamilyNumber()
    {
        var root = FindRoot();

        var page = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Edit.cshtml"));

        var model = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Edit.cshtml.cs"));

        var identityBootstrap = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "PeopleIdentityBootstrap.cs"));

        Assert.Contains(
            "asp-for=\"FamilyNumber\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-for=\"FamilyIdentityDocumentId\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "LoadFamilyNumberAsync(Employee.Id)",
            model,
            StringComparison.Ordinal);
        Assert.Contains(
            "SaveFamilyNumberAsync(Employee.Id)",
            model,
            StringComparison.Ordinal);
        Assert.Contains(
            "PeopleIdentityBootstrap.SetFamilyNumberAsync",
            model,
            StringComparison.Ordinal);
        Assert.Contains(
            "dbo.EmployeeIdentityDocuments",
            identityBootstrap,
            StringComparison.Ordinal);
        Assert.Contains(
            "SET FamilyNumber = @FamilyNumber",
            identityBootstrap,
            StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SmartAttendance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "SmartAttendance repository root was not found.");
    }
}
