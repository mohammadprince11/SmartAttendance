using Xunit;

namespace SmartAttendance.Tests;

public sealed class ProvisionCompanyScopeTests
{
    [Fact]
    public void EmployeeBalancesAndLeaves_AreAllCompanyScopedInSql()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "ProvisionCalculator.cs"));

        Assert.Contains("ApplicationDbContext db, CompanyScope scope", source, StringComparison.Ordinal);
        Assert.True(Count(source, "EmployeeCompanyGuard.ListFilter(scope") >= 3);
        Assert.DoesNotContain("employees = employees.Where", source, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}
