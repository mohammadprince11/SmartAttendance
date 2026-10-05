using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class EmployeeUpdateHistoryMigrationTests
{
    private const string MigrationId = "20260902-01-employee-update-history";

    [Fact]
    public void History_tables_have_a_new_additive_migration()
    {
        var migration = Assert.Single(SqlSchemaMigrator.Migrations, item => item.Id == MigrationId);
        Assert.Contains("CREATE TABLE dbo.EmployeeUpdateBatches", migration.Sql);
        Assert.Contains("CREATE TABLE dbo.EmployeeUpdateChanges", migration.Sql);
        Assert.Contains("BEGIN TRANSACTION;", migration.Sql);
        Assert.Contains("ROLLBACK TRANSACTION;", migration.Sql);
        Assert.DoesNotContain("DROP TABLE", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", migration.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exported_sql_matches_the_registered_migration()
    {
        var migration = Assert.Single(SqlSchemaMigrator.Migrations, item => item.Id == MigrationId);
        var exported = File.ReadAllText(Path.Combine(FindRoot(),
            "database", "migrations", MigrationId + ".sql"));
        Assert.Equal(migration.Sql.Replace("\r\n", "\n").Trim(), exported.Replace("\r\n", "\n").Trim());
    }

    [Fact]
    public void Profile_timeline_does_not_create_its_tables_during_a_request()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(),
            "SmartAttendance.Web", "Pages", "Employees", "Profile.Timeline.cshtml.cs"));
        Assert.DoesNotContain("EmployeeUpdateSchema.EnsureAsync", source);
        Assert.DoesNotContain("CREATE TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
