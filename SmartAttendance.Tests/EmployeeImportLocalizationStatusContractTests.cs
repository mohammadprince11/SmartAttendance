using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeImportLocalizationStatusContractTests
{
    [Fact]
    public void EmployeeImporter_UsesDatabaseAllowedTranslationStatus()
    {
        var root = FindRoot();
        var path = Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Imports",
            "EmployeeBootstrapImportEngine.cs");

        var source = File.ReadAllText(path);

        Assert.DoesNotContain(
            "TranslationStatus = \"Import\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "TranslationStatus = \"Manual\"",
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