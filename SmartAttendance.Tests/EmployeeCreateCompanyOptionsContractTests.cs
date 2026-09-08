using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeCreateCompanyOptionsContractTests
{
    [Fact]
    public void CreatePage_DoesNotDependOnBranchesToShowCompanyOrNameLanguages()
    {
        var root = FindRoot();

        var modelPath = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Create.cshtml.cs");

        var viewPath = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Create.cshtml");

        var model = File.ReadAllText(modelPath);
        var view = File.ReadAllText(viewPath);

        Assert.Contains(
            ".Where(item => item.IsActive && !item.IsDeleted)",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "CompanyOptions.Select(item => item.Id)",
            model,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "var companyIds = branches",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "Model.CompanyOptions.Select(item => item.Id)",
            view,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Model.Branches.Select(item => item.CompanyId).Distinct()",
            view,
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