namespace SmartAttendance.Tests;

public sealed class ZynoraBrandMigrationContractTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Compatibility_migration_preflights_every_pair_before_schema_changes()
    {
        var source = Read("SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var start = source.IndexOf("20260904-01-zynora-brand-table-compatibility", StringComparison.Ordinal);
        Assert.True(start > 0);
        var migration = source[start..];

        var conflict = migration.IndexOf("THROW 51031", StringComparison.Ordinal);
        var transaction = migration.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        var firstRename = migration.IndexOf("sys.sp_rename", StringComparison.Ordinal);
        var firstCreate = migration.IndexOf("CREATE TABLE dbo.Zynora", StringComparison.Ordinal);

        Assert.True(conflict > 0 && conflict < transaction);
        Assert.True(transaction < firstRename && firstRename < firstCreate);
        Assert.Contains("SET XACT_ABORT ON", migration, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION", migration, StringComparison.Ordinal);
        Assert.Contains("No automatic merge or deletion was attempted", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_paths_do_not_rename_or_create_brand_tables()
    {
        var settingsStore = Read(
            "SmartAttendance.Web", "Infrastructure", "HrSettings", "HrSettingsStore.cs");
        var notificationGenerator = Read(
            "SmartAttendance.Web", "Infrastructure", "Notifications", "NotificationRuleGenerator.cs");

        Assert.DoesNotContain("sp_rename", settingsStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE Zynora", settingsStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_rename", notificationGenerator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE Zynora", notificationGenerator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Active_runtime_queries_use_current_brand_table_names()
    {
        var settingsStore = Read(
            "SmartAttendance.Web", "Infrastructure", "HrSettings", "HrSettingsStore.cs");
        var notificationGenerator = Read(
            "SmartAttendance.Web", "Infrastructure", "Notifications", "NotificationRuleGenerator.cs");

        Assert.Contains("ZynoraHrSettings", settingsStore, StringComparison.Ordinal);
        Assert.Contains("ZynoraTerminationReasons", settingsStore, StringComparison.Ordinal);
        Assert.Contains("ZynoraNotificationRules", settingsStore, StringComparison.Ordinal);
        Assert.Contains("ZynoraNotificationEvents", notificationGenerator, StringComparison.Ordinal);
    }
}
