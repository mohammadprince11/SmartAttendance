using Xunit;

namespace SmartAttendance.Tests;

public sealed class SalaryItemCompanyScopeTests
{
    [Fact]
    public void SalaryItems_AreCompanyScopedAcrossCatalogAndPayrollConsumers()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SalaryItemStore.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var runStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Payroll", "SalaryItems.cshtml.cs");

        Assert.Contains("20260826-01-salary-item-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("CompanyId int NULL", migration, StringComparison.Ordinal);
        Assert.Contains("scope.ToSqlPredicate(\"CompanyId\")", store, StringComparison.Ordinal);
        Assert.Contains("IsSystem = 1 AND CompanyId IS NULL", store, StringComparison.Ordinal);
        Assert.Contains("item.CompanyId is not > 0 || !scope.Allows(item.CompanyId)", store, StringComparison.Ordinal);
        Assert.Contains("runScope, run.CompanyId", runStore, StringComparison.Ordinal);
        Assert.Contains("ICompanyScopeProvider", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SalaryItemStore.ListAsync(_db);", page, StringComparison.Ordinal);
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
