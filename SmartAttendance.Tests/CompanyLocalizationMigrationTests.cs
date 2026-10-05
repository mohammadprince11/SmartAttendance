using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class CompanyLocalizationMigrationTests
{
    public const string MigrationId = "20260902-02-company-data-localization";

    [Fact]
    public void Startup_registers_both_localization_tables_in_an_additive_migration()
    {
        var migration = Assert.Single(SqlSchemaMigrator.Migrations, item => item.Id == MigrationId);
        Assert.Contains("CREATE TABLE dbo.CompanyLanguages", migration.Sql);
        Assert.Contains("CREATE TABLE dbo.LocalizedEntityValues", migration.Sql);
        Assert.Contains("BEGIN TRANSACTION;", migration.Sql);
        Assert.Contains("ROLLBACK TRANSACTION;", migration.Sql);
        Assert.DoesNotContain("DROP TABLE", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", migration.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", migration.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_matches_registered_sql_and_keeps_company_scoped_indexes()
    {
        var migration = Assert.Single(SqlSchemaMigrator.Migrations, item => item.Id == MigrationId);
        var exported = File.ReadAllText(Path.Combine(FindRoot(), "database", "migrations", MigrationId + ".sql"));
        Assert.Equal(migration.Sql.Replace("\r\n", "\n").Trim(), exported.Replace("\r\n", "\n").Trim());
        Assert.Contains("UX_CompanyLanguages_OneDefault", migration.Sql);
        Assert.Contains("WHERE IsDefault = 1 AND IsActive = 1 AND IsDeleted = 0", migration.Sql);
        Assert.Contains("LocalizedEntityValues(CompanyId, EntityType, EntityId, FieldName, CultureCode)", migration.Sql);
        Assert.Contains("REFERENCES dbo.Companies(Id)", migration.Sql);
    }

    [Fact]
    public void Reading_languages_does_not_attempt_request_time_schema_creation()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "Infrastructure",
            "Localization", "CompanyDataLocalizationService.cs"));
        Assert.DoesNotContain("CREATE TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlSchemaMigrator.ApplyAsync", source);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
