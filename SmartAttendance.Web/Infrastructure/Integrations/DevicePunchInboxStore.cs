using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Integrations;

public static class DevicePunchInboxStore
{
    public sealed record Heartbeat(
        string ConnectorKey, DateTime LastSeenAt, DateTime? LastSuccessAt,
        string? LastError, int LastBatchCount, int Pending, int DeadLetter);
    public sealed record Punch(
        string ExternalId, string EmployeeNo, DateTimeOffset PunchedAt,
        string? PunchType, string? DeviceCode);

    public sealed record IngestResult(int Accepted, int Duplicate, int DeadLetter);

    public static Task<List<Heartbeat>> ListHeartbeatsAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Connector company is outside the effective scope.");
        return HrmsDatabase.QueryAsync(db, $"""
SELECT h.ConnectorKey,h.LastSeenAt,h.LastSuccessAt,h.LastError,h.LastBatchCount,
       (SELECT COUNT(*) FROM DevicePunchInbox i WHERE i.CompanyId=h.CompanyId
          AND i.ConnectorKey=h.ConnectorKey AND i.Status IN (N'Pending',N'Retry',N'Processing')) Pending,
       (SELECT COUNT(*) FROM DevicePunchInbox i WHERE i.CompanyId=h.CompanyId
          AND i.ConnectorKey=h.ConnectorKey AND i.Status=N'DeadLetter') DeadLetter
FROM DeviceConnectorHeartbeats h
WHERE h.CompanyId=@CompanyId AND {scope.ToSqlPredicate("h.CompanyId")}
ORDER BY h.ConnectorKey;
""", command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new Heartbeat(
                HrmsDatabase.GetString(reader, "ConnectorKey"),
                Convert.ToDateTime(reader["LastSeenAt"]), HrmsDatabase.GetDateTime(reader, "LastSuccessAt"),
                HrmsDatabase.GetString(reader, "LastError") is { Length: > 0 } error ? error : null,
                HrmsDatabase.GetInt(reader, "LastBatchCount"), HrmsDatabase.GetInt(reader, "Pending"),
                HrmsDatabase.GetInt(reader, "DeadLetter")));
    }

    public static Task RetryDeadLettersAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, string? connectorKey = null)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Connector company is outside the effective scope.");
        return HrmsDatabase.ExecuteAsync(db, $"""
UPDATE DevicePunchInbox
SET Status=N'Retry',AttemptCount=0,NextAttemptAt=SYSUTCDATETIME(),LastError=NULL
WHERE CompanyId=@CompanyId AND Status=N'DeadLetter'
  AND (@Connector IS NULL OR ConnectorKey=@Connector)
  AND {scope.ToSqlPredicate("CompanyId")};
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Connector",
                string.IsNullOrWhiteSpace(connectorKey) ? DBNull.Value : connectorKey.Trim());
        });
    }

    public static async Task<IngestResult> IngestAsync(
        ApplicationDbContext db, IntegrationApiKeyStore.Identity identity,
        string connectorKey, IReadOnlyList<Punch> punches)
    {
        var accepted = 0;
        var duplicate = 0;
        var deadLetter = 0;
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var punch in punches)
        {
            var error = Validate(punch);
            var status = error is null ? "Pending" : "DeadLetter";
            var payload = JsonSerializer.Serialize(punch);
            var affected = await HrmsDatabase.ScalarAsync<int>(db, """
IF NOT EXISTS (
    SELECT 1 FROM DevicePunchInbox WITH (UPDLOCK,HOLDLOCK)
    WHERE CompanyId=@CompanyId AND IntegrationKeyId=@KeyId AND ExternalId=@ExternalId)
BEGIN
    INSERT INTO DevicePunchInbox
        (CompanyId,IntegrationKeyId,ConnectorKey,ExternalId,EmployeeNo,PunchedAt,PunchType,
         DeviceCode,PayloadHash,Status,AttemptCount,LastError,CreatedAt)
    VALUES
        (@CompanyId,@KeyId,@Connector,@ExternalId,@EmployeeNo,@PunchedAt,@PunchType,
         @DeviceCode,@PayloadHash,@Status,0,@Error,SYSUTCDATETIME());
    SELECT 1;
END
ELSE SELECT 0;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", identity.CompanyId);
                HrmsDatabase.AddParameter(command, "@KeyId", identity.Id);
                HrmsDatabase.AddParameter(command, "@Connector", connectorKey);
                HrmsDatabase.AddParameter(command, "@ExternalId", punch.ExternalId.Trim());
                HrmsDatabase.AddParameter(command, "@EmployeeNo", punch.EmployeeNo.Trim());
                HrmsDatabase.AddParameter(command, "@PunchedAt", punch.PunchedAt);
                HrmsDatabase.AddParameter(command, "@PunchType", string.IsNullOrWhiteSpace(punch.PunchType) ? DBNull.Value : punch.PunchType.Trim());
                HrmsDatabase.AddParameter(command, "@DeviceCode", string.IsNullOrWhiteSpace(punch.DeviceCode) ? DBNull.Value : punch.DeviceCode.Trim());
                HrmsDatabase.AddParameter(command, "@PayloadHash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant());
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@Error", error is null ? DBNull.Value : error);
            });
            if (affected == 0) duplicate++;
            else if (error is null) accepted++;
            else deadLetter++;
        }

        await HrmsDatabase.ExecuteAsync(db, """
MERGE DeviceConnectorHeartbeats AS target
USING (SELECT @CompanyId CompanyId,@Connector ConnectorKey) AS source
ON target.CompanyId=source.CompanyId AND target.ConnectorKey=source.ConnectorKey
WHEN MATCHED THEN UPDATE SET LastSeenAt=SYSUTCDATETIME(),LastSuccessAt=SYSUTCDATETIME(),
    LastError=NULL,LastBatchCount=@Count,UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (CompanyId,ConnectorKey,LastSeenAt,LastSuccessAt,LastBatchCount,CreatedAt)
    VALUES(@CompanyId,@Connector,SYSUTCDATETIME(),SYSUTCDATETIME(),@Count,SYSUTCDATETIME());
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", identity.CompanyId);
            HrmsDatabase.AddParameter(command, "@Connector", connectorKey);
            HrmsDatabase.AddParameter(command, "@Count", punches.Count);
        });
        await transaction.CommitAsync();
        return new IngestResult(accepted, duplicate, deadLetter);
    }

    private static string? Validate(Punch punch)
    {
        if (string.IsNullOrWhiteSpace(punch.ExternalId) || punch.ExternalId.Length > 200)
            return "ExternalId is required and must be at most 200 characters.";
        if (string.IsNullOrWhiteSpace(punch.EmployeeNo) || punch.EmployeeNo.Length > 100)
            return "EmployeeNo is required and must be at most 100 characters.";
        if (punch.PunchedAt < DateTimeOffset.UtcNow.AddYears(-2) ||
            punch.PunchedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            return "PunchedAt is outside the accepted ingestion window.";
        if (!string.IsNullOrWhiteSpace(punch.PunchType) &&
            punch.PunchType is not ("In" or "Out" or "Break" or "Unknown"))
            return "PunchType is invalid.";
        return null;
    }
}
