using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeImportLocalizedValuesCollectionContractTests
{
    [Fact]
    public void EmployeeImporter_DoesNotMutateEmployeeValuesWhileEnumeratingThem()
    {
        var root = FindRoot();
        var path = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Imports",
            "EmployeeBootstrapImportEngine.cs");

        var source = File.ReadAllText(path);

        Assert.Contains(
            "var employeeCultures = employeeValues",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            ".ToArray();",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "foreach (var culture in employeeCultures)",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "foreach (var culture in employeeValues",
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