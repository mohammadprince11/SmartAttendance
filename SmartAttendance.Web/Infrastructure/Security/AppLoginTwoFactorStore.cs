using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

public sealed record AppLoginTwoFactorState(
    bool IsEnabled,
    string? ActiveSecretProtected,
    string? PendingSecretProtected,
    IReadOnlyList<string> RecoveryCodeHashes)
{
    public static AppLoginTwoFactorState Empty { get; } =
        new(false, null, null, Array.Empty<string>());
}

public static class AppLoginTwoFactorStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AppLoginTwoFactorState> GetAsync(
        ApplicationDbContext db,
        int loginUserId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            if (db.Database.CurrentTransaction is { } current)
                command.Transaction = current.GetDbTransaction();
            command.CommandText = """
SELECT IsEnabled, ActiveSecretProtected, PendingSecretProtected, RecoveryCodeHashesJson
FROM AppLoginTwoFactor
WHERE LoginUserId = @Id;
""";
            AddParameter(command, "@Id", loginUserId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return AppLoginTwoFactorState.Empty;

            var hashes = reader.IsDBNull(3)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(reader.GetString(3), Json)
                    ?? Array.Empty<string>();

            return new AppLoginTwoFactorState(
                reader.GetBoolean(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                hashes);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
    public static async Task BeginSetupAsync(
        ApplicationDbContext db,
        int loginUserId,
        string pendingSecretProtected,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
IF EXISTS (SELECT 1 FROM AppLoginTwoFactor WHERE LoginUserId=@Id)
    UPDATE AppLoginTwoFactor
       SET PendingSecretProtected=@Pending,
           UpdatedAtUtc=SYSUTCDATETIME()
     WHERE LoginUserId=@Id;
ELSE
    INSERT INTO AppLoginTwoFactor
        (LoginUserId, IsEnabled, PendingSecretProtected, UpdatedAtUtc)
    VALUES
        (@Id, 0, @Pending, SYSUTCDATETIME());
""";
        await ExecuteAsync(
            db,
            sql,
            command =>
            {
                AddParameter(command, "@Id", loginUserId);
                AddParameter(command, "@Pending", pendingSecretProtected);
            },
            cancellationToken);
    }

    public static async Task EnablePendingAsync(
        ApplicationDbContext db,
        int loginUserId,
        IReadOnlyCollection<string> recoveryCodeHashes,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(recoveryCodeHashes, Json);
        const string sql = """
UPDATE AppLoginTwoFactor
   SET ActiveSecretProtected=PendingSecretProtected,
       PendingSecretProtected=NULL,
       IsEnabled=1,
       RecoveryCodeHashesJson=@Recovery,
       EnabledAtUtc=SYSUTCDATETIME(),
       UpdatedAtUtc=SYSUTCDATETIME()
 WHERE LoginUserId=@Id
   AND PendingSecretProtected IS NOT NULL;
""";
        await ExecuteAsync(
            db,
            sql,
            command =>
            {
                AddParameter(command, "@Id", loginUserId);
                AddParameter(command, "@Recovery", json);
            },
            cancellationToken);
    }

    public static Task DisableAsync(
        ApplicationDbContext db,
        int loginUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            db,
            """
UPDATE AppLoginTwoFactor
   SET IsEnabled=0,
       ActiveSecretProtected=NULL,
       PendingSecretProtected=NULL,
       RecoveryCodeHashesJson=NULL,
       EnabledAtUtc=NULL,
       UpdatedAtUtc=SYSUTCDATETIME()
 WHERE LoginUserId=@Id;
""",
            command => AddParameter(command, "@Id", loginUserId),
            cancellationToken);
    public static async Task ReplaceRecoveryCodesAsync(
        ApplicationDbContext db,
        int loginUserId,
        IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(hashes, Json);
        await ExecuteAsync(
            db,
            """
UPDATE AppLoginTwoFactor
   SET RecoveryCodeHashesJson=@Recovery,
       UpdatedAtUtc=SYSUTCDATETIME()
 WHERE LoginUserId=@Id AND IsEnabled=1;
""",
            command =>
            {
                AddParameter(command, "@Id", loginUserId);
                AddParameter(command, "@Recovery", json);
            },
            cancellationToken);
    }

    public static async Task<bool> ConsumeRecoveryCodeAsync(
        ApplicationDbContext db,
        int loginUserId,
        string recoveryCode,
        CancellationToken cancellationToken = default)
    {
        var suppliedHash = TotpSecurity.HashRecoveryCode(recoveryCode);
        await using var transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var state = await GetAsync(db, loginUserId, cancellationToken);
        var matched = state.RecoveryCodeHashes.FirstOrDefault(hash =>
            FixedTimeEqualsHex(hash, suppliedHash));
        if (matched is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var remaining = state.RecoveryCodeHashes
            .Where(hash => !string.Equals(hash, matched, StringComparison.Ordinal))
            .ToArray();

        await ReplaceRecoveryCodesAsync(db, loginUserId, remaining, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool FixedTimeEqualsHex(string left, string right)
    {
        try
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExecuteAsync(
        ApplicationDbContext db,
        string sql,
        Action<System.Data.Common.DbCommand> bind,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (db.Database.CurrentTransaction is { } current)
                command.Transaction = current.GetDbTransaction();
            bind(command);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
