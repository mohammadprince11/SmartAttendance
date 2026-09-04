using Xunit;

namespace SmartAttendance.Tests;

public class EmployeeCreateSalaryContractTests
{
    [Fact]
    public void CreatePage_ShowsBasicSalaryOnlyForCompensationEditors()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml"));

        Assert.Contains("@if (Model.CanEditCompensation)", page, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"BasicSalary\"", page, StringComparison.Ordinal);
        Assert.Contains("الراتب الأساسي", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateHandler_AuthorizesAndPersistsBasicSalaryInFinancialProfile()
    {
        var root = FindRepositoryRoot();
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));

        Assert.Contains("PeoplePermissionCodes.EditCompensation", model, StringComparison.Ordinal);
        Assert.Contains("HasGlobalPermissionAsync", model, StringComparison.Ordinal);
        Assert.Contains("SaveBasicSalaryAsync(employeeId)", model, StringComparison.Ordinal);
        Assert.Contains("dbo.EmployeeFinancialInfos", model, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeTemplate_AppendsCreatePageFieldsAfterLegacySalaryColumn()
    {
        var root = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Imports",
            "EmployeeBootstrapImportEngine.cs"));

        var salary = engine.IndexOf("new(\"BasicSalary\"", StringComparison.Ordinal);
        var firstName = engine.IndexOf("new(\"FirstName\"", StringComparison.Ordinal);
        var personalEmail = engine.IndexOf("new(\"PersonalEmail\"", StringComparison.Ordinal);

        Assert.True(salary >= 0);
        Assert.True(firstName > salary);
        Assert.True(personalEmail > firstName);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
