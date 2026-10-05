using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

if (args.Length != 1 || args[0] is not ("--status" or "--apply"))
{
    throw new InvalidOperationException(
        "Usage: SmartAttendance.DatabaseMigrator --status|--apply");
}

var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ZYNORA_DATABASE_MIGRATION_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings__DefaultConnection or ZYNORA_DATABASE_MIGRATION_CONNECTION is required.");
}

var connectionInfo = new SqlConnectionStringBuilder(connectionString);
Console.WriteLine($"Target server: {connectionInfo.DataSource}");
Console.WriteLine($"Target database: {connectionInfo.InitialCatalog}");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connectionString)
    .Options;
await using var db = new ApplicationDbContext(options);
var applied = await LoadAppliedMigrationIdsAsync(db);
var pending = SqlSchemaMigrator.Migrations
    .Where(migration => !applied.Contains(migration.Id))
    .ToArray();

Console.WriteLine($"Applied migrations: {applied.Count}");
Console.WriteLine($"Pending migrations: {pending.Length}");

foreach (var migration in pending)
{
    Console.WriteLine($"PENDING {migration.Id}");
}

Console.WriteLine(
    $"People AI foundation: {(applied.Contains(PeopleAiSchema.FoundationMigrationId) ? "APPLIED" : "PENDING")}");

if (args[0] == "--status")
{
    return;
}

var confirmation =
    Environment.GetEnvironmentVariable("ZYNORA_DATABASE_MIGRATION_CONFIRM");
if (!string.Equals(confirmation, "APPLY", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Set ZYNORA_DATABASE_MIGRATION_CONFIRM=APPLY before --apply.");
}
var backupReference =
    Environment.GetEnvironmentVariable("ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE");
if (string.IsNullOrWhiteSpace(backupReference))
{
    throw new InvalidOperationException(
        "ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE is required before --apply.");
}

Console.WriteLine($"Backup reference confirmed: {backupReference}");

if (pending.Length == 0)
{
    await PeopleAiSchema.VerifyAsync(db);
    Console.WriteLine("No pending controlled migrations. People AI schema verified.");
    return;
}

await DatabaseDeployment.ApplyAsync(db);
await PeopleAiSchema.VerifyAsync(db);

var after = await LoadAppliedMigrationIdsAsync(db);
if (!after.Contains(PeopleAiSchema.FoundationMigrationId))
{
    throw new InvalidOperationException(
        "People AI foundation migration was not recorded after deployment.");
}

Console.WriteLine("Controlled database deployment completed successfully.");
Console.WriteLine($"Recorded migration: {PeopleAiSchema.FoundationMigrationId}");
static async Task<HashSet<string>> LoadAppliedMigrationIdsAsync(
    ApplicationDbContext db)
{
    var connection = db.Database.GetDbConnection();
    var openedHere =
        connection.State != System.Data.ConnectionState.Open;

    if (openedHere)
    {
        await connection.OpenAsync();
    }

    try
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText =
            "SELECT CASE WHEN OBJECT_ID('dbo.__SchemaMigrations','U') IS NULL THEN 0 ELSE 1 END;";

        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT MigrationId FROM dbo.__SchemaMigrations ORDER BY MigrationId;";

        await using var reader = await command.ExecuteReaderAsync();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
    finally
    {
        if (openedHere)
        {
            await connection.CloseAsync();
        }
    }
}
