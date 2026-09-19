using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiReadinessCreateTests
{
    [Fact]
    public void EmployeeCreate_InitialDocumentsUseProtectedStorage()
    {
        var model = File.ReadAllText(Path.Combine(
            FindRoot(), "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));

        Assert.Contains("_protectedFiles.SaveAsync(", model, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "uploads\", \"employee-documents",
            model,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmployeeCreate_EnforcesEffectiveLocationScope()
    {
        var model = File.ReadAllText(Path.Combine(
            FindRoot(), "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));

        Assert.Contains("IEffectiveScopeService", model, StringComparison.Ordinal);
        Assert.Contains("_createScope.AllowsLocation(", model, StringComparison.Ordinal);
        Assert.Contains(
            "لا تملك صلاحية إنشاء موظف",
            model,
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

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
