using Xunit;

namespace SmartAttendance.Tests;

public sealed class PayrollSimulatorCompanyScopeTests
{
    [Fact]
    public void EmployeeFactsNamesCustomFieldsAndAssignments_AreScopedBeforeMaterialization()
    {
        var root = FindRoot();
        var simulator = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Payroll", "Simulator.cshtml.cs"));
        var facts = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "HrConditionFacts.cs"));

        Assert.Contains("authorizationScope: scope", simulator, StringComparison.Ordinal);
        Assert.True(Count(simulator, "EmployeeCompanyGuard.ListFilter(scope") >= 2);
        Assert.DoesNotContain("FilterEmployeesInScopeAsync", simulator, StringComparison.Ordinal);
        Assert.True(Count(facts, "EmployeeCompanyGuard.ListFilter(authorizationScope") >= 2);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}
