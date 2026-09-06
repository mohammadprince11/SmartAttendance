using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeImportCompanyAssignmentContractTests
{
    [Fact]
    public void Import_AssignsCompanyIdBeforeSavingEmployees()
    {
        var root = FindRoot();
        var path = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Imports",
            "EmployeeBootstrapImportEngine.cs");

        var source = File.ReadAllText(path);

        var companyAssignment = source.IndexOf(
            "employee.CompanyId = company.Id;",
            StringComparison.Ordinal);

        var employeeSave = source.IndexOf(
            "await _dbContext.SaveChangesAsync();",
            companyAssignment,
            StringComparison.Ordinal);

        Assert.True(
            companyAssignment >= 0,
            "Employee import must assign employee.CompanyId from the resolved company.");

        Assert.True(
            employeeSave > companyAssignment,
            "CompanyId must be assigned before the employee SaveChanges call.");
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