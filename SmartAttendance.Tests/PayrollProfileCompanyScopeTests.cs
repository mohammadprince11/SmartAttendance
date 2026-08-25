using Xunit;

namespace SmartAttendance.Tests;

public sealed class PayrollProfileCompanyScopeTests
{
    [Fact]
    public void TaxAndGosiProfiles_AreCompanyScopedAcrossConfigurationEmployeesAndRuns()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollConfigStore.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var settings = Read(root, "SmartAttendance.Web", "Pages", "Payroll", "Settings.cshtml.cs");
        var runStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs");
        var financial = Read(root, "SmartAttendance.Web", "Pages", "Employees", "FinancialInfo.cshtml.cs");

        Assert.Contains("20260826-03-payroll-profile-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("ProfilePredicate(scope, companyId", store, StringComparison.Ordinal);
        Assert.Contains("scope.ToSqlPredicate(\"CompanyId\")", store, StringComparison.Ordinal);
        Assert.Contains("!scope.Allows(profile.CompanyId)", store, StringComparison.Ordinal);
        Assert.Contains("ICompanyScopeProvider", settings, StringComparison.Ordinal);
        Assert.Contains("runCompanyForLoans", runStore, StringComparison.Ordinal);
        Assert.Contains("allowedTaxProfiles.All", financial, StringComparison.Ordinal);
        Assert.DoesNotContain("ListTaxProfilesAsync(_dbContext);", financial, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
