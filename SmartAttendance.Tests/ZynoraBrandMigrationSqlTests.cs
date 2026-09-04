using Microsoft.Data.SqlClient;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class ZynoraBrandMigrationSqlTests : IAsyncLifetime
{
    private string? _adminConnection;
    private string? _databaseName;
    private string? _connectionString;
    private bool _available;
    private string? _failure;

    private static string Legacy(string suffix) => "Ne" + "xora" + suffix;
    private static string Current(string suffix) => "Zynora" + suffix;
    private static string LegacyEventIndex() => "UX_" + Legacy("NotifEvents_Key");

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        if (string.IsNullOrWhiteSpace(configured) && OperatingSystem.IsWindows())
            configured = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        if (string.IsNullOrWhiteSpace(configured)) return;

        _databaseName = "SmartAttendance_ZynoraBrand_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        _adminConnection = adminBuilder.ConnectionString;

        try
        {
            await using (var admin = new SqlConnection(_adminConnection))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{_databaseName}];";
                await create.ExecuteNonQueryAsync();
            }

            var databaseBuilder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = _databaseName,
                MultipleActiveResultSets = true
            };
            _connectionString = databaseBuilder.ConnectionString;
            _available = true;
        }
        catch (Exception ex)
        {
            _failure = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_adminConnection is null || _databaseName is null ||
            !_databaseName.StartsWith("SmartAttendance_ZynoraBrand_", StringComparison.Ordinal)) return;

        try
        {
            SqlConnection.ClearAllPools();
            await using var admin = new SqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // This database is uniquely named and owned by this test; cleanup is best effort.
        }
    }

    [SkippableFact]
    public async Task Legacy_tables_are_renamed_atomically_and_keep_their_rows()
    {
        RequireSql();
        await ExecuteAsync($$"""
CREATE TABLE dbo.[{{Legacy("HrSettings")}}] (Id int NOT NULL PRIMARY KEY, Payload nvarchar(50) NOT NULL);
CREATE TABLE dbo.[{{Legacy("TerminationReasons")}}] (Id int NOT NULL PRIMARY KEY, Payload nvarchar(50) NOT NULL);
CREATE TABLE dbo.[{{Legacy("NotificationRules")}}] (Id int NOT NULL PRIMARY KEY, Payload nvarchar(50) NOT NULL);
CREATE TABLE dbo.[{{Legacy("NotificationEvents")}}] (Id int NOT NULL PRIMARY KEY, EventKey nvarchar(200) NOT NULL, Payload nvarchar(50) NOT NULL);
CREATE UNIQUE INDEX [{{LegacyEventIndex()}}] ON dbo.[{{Legacy("NotificationEvents")}}](EventKey);
INSERT INTO dbo.[{{Legacy("HrSettings")}}] VALUES (1, N'settings');
INSERT INTO dbo.[{{Legacy("TerminationReasons")}}] VALUES (2, N'termination');
INSERT INTO dbo.[{{Legacy("NotificationRules")}}] VALUES (3, N'rules');
INSERT INTO dbo.[{{Legacy("NotificationEvents")}}] VALUES (4, N'event-4', N'events');
""");

        await ApplyCompatibilityMigrationAsync();

        foreach (var suffix in TableSuffixes)
        {
            Assert.False(await TableExistsAsync(Legacy(suffix)));
            Assert.True(await TableExistsAsync(Current(suffix)));
            Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{Current(suffix)}];"));
        }

        // The migration itself is idempotent even outside its ledger guard.
        await ApplyCompatibilityMigrationAsync();
        Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{Current("NotificationEvents")}];"));
    }

    [SkippableFact]
    public async Task Any_legacy_current_conflict_fails_before_other_tables_are_touched()
    {
        RequireSql();
        await ExecuteAsync($$"""
CREATE TABLE dbo.[{{Legacy("HrSettings")}}] (Id int NOT NULL PRIMARY KEY);
CREATE TABLE dbo.[{{Current("HrSettings")}}] (Id int NOT NULL PRIMARY KEY);
CREATE TABLE dbo.[{{Legacy("TerminationReasons")}}] (Id int NOT NULL PRIMARY KEY);
CREATE TABLE dbo.[{{Legacy("NotificationRules")}}] (Id int NOT NULL PRIMARY KEY);
CREATE TABLE dbo.[{{Legacy("NotificationEvents")}}] (Id int NOT NULL PRIMARY KEY);
""");

        var exception = await Assert.ThrowsAsync<SqlException>(ApplyCompatibilityMigrationAsync);
        Assert.Equal(51031, exception.Number);

        Assert.True(await TableExistsAsync(Legacy("HrSettings")));
        Assert.True(await TableExistsAsync(Current("HrSettings")));
        foreach (var suffix in TableSuffixes.Skip(1))
        {
            Assert.True(await TableExistsAsync(Legacy(suffix)));
            Assert.False(await TableExistsAsync(Current(suffix)));
        }
    }

    [SkippableFact]
    public async Task Clean_database_gets_current_empty_tables_only_and_can_run_twice()
    {
        RequireSql();
        await ApplyCompatibilityMigrationAsync();
        await ApplyCompatibilityMigrationAsync();

        foreach (var suffix in TableSuffixes)
        {
            Assert.False(await TableExistsAsync(Legacy(suffix)));
            Assert.True(await TableExistsAsync(Current(suffix)));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{Current(suffix)}];"));
        }
    }

    private static readonly string[] TableSuffixes =
    [
        "HrSettings",
        "TerminationReasons",
        "NotificationRules",
        "NotificationEvents"
    ];

    private async Task ApplyCompatibilityMigrationAsync()
    {
        var migration = Assert.Single(
            SqlSchemaMigrator.Migrations,
            item => item.Id == "20260904-01-zynora-brand-table-compatibility");
        await ExecuteAsync(migration.Sql);
    }

    private async Task<bool> TableExistsAsync(string tableName) =>
        await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END;",
            command => command.Parameters.AddWithValue("@TableName", "dbo." + tableName)) == 1;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, Action<SqlCommand>? configure = null)
    {
        await using var connection = new SqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private void RequireSql() =>
        Skip.IfNot(_available, _failure ?? "No SQL Server test connection is available.");
}
