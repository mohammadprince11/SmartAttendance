namespace SmartAttendance.Tests;

public sealed class AttendanceSourceCompanyScopeTests
{
    [Fact]
    public void AttendanceSources_AreCompanyScopedAtStoreAndPageBoundaries()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "AttendanceSourceStore.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "AttendanceSettings", "Index.cshtml.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));

        Assert.Contains("20260826-04-attendance-source-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("scope.ToSqlPredicate(\"CompanyId\")", store, StringComparison.Ordinal);
        Assert.Contains("CompanyId = @CompanyId", store, StringComparison.Ordinal);
        Assert.Contains("!scope.Allows", store, StringComparison.Ordinal);
        Assert.Contains("AttendanceSourceStore.ListAsync(_dbContext, scope, CompanyId)", page, StringComparison.Ordinal);
        Assert.Contains("AttendanceSourceStore.SaveAsync(_dbContext, scope, source)", page, StringComparison.Ordinal);
        Assert.Contains("AttendanceSourceStore.DeleteAsync(_dbContext, scope, companyId, id)", page, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
