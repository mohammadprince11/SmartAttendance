using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleReportsCompanyScopeTests
{
    [Fact]
    public void SavedReports_EnforceCompanyScopeBeforeMaterializationAndMutation()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Reports", "PeopleReportsStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "PeopleReports", "Index.cshtml.cs");
        var portal = Read(root, "SmartAttendance.Web", "Pages", "EmployeePortal", "Reports.cshtml.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");

        Assert.Contains("20260826-02-people-report-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("20260826-08-report-group-sort", migration, StringComparison.Ordinal);
        Assert.Contains("scope.ToSqlPredicate(\"CompanyId\")", store, StringComparison.Ordinal);
        Assert.Contains("IsSystem = 1 AND CompanyId IS NULL", store, StringComparison.Ordinal);
        Assert.Contains("!scope.Allows(companyId)", store, StringComparison.Ordinal);
        Assert.Contains("LoadAllAsync(_dbContext, scope)", page, StringComparison.Ordinal);
        Assert.Contains("PeopleReportsStore.GetAsync(_dbContext, scope", page, StringComparison.Ordinal);
        Assert.Contains("AllowedShareUsersAsync(scope, companyId)", page, StringComparison.Ordinal);
        Assert.Contains("LoadAllAsync(_dbContext, employeeScope)", portal, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAllAsync(_dbContext);", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAllAsync(_dbContext);", portal, StringComparison.Ordinal);
        Assert.Contains("ValidKey(groupColumnKey, dataset)", page, StringComparison.Ordinal);
        Assert.Contains("ValidKey(sortColumnKey, dataset)", page, StringComparison.Ordinal);
        Assert.Contains("GroupColumnKey = @GroupColumn", store, StringComparison.Ordinal);
        Assert.Contains("SortColumnKey = @SortColumn", store, StringComparison.Ordinal);
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
