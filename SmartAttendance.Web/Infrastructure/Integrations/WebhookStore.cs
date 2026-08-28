using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Integrations;

/// <summary>صندوق صادر durable للـwebhooks. المخطط تنشئه هجرة الإقلاع فقط.</summary>
public static class WebhookStore
{
    public sealed record Subscription(
        int Id, int CompanyId, string Name, string EndpointUrl, string EventsCsv,
        bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

    public sealed record Delivery(
        long Id, int SubscriptionId, int CompanyId, string EndpointUrl, string ProtectedSecret,
        string EventType, string PayloadJson, string IdempotencyKey, int AttemptCount);

    public static Task<List<Subscription>> ListSubscriptionsAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Webhook company is outside the effective scope.");
        return HrmsDatabase.QueryAsync(db, $"""
SELECT Id, CompanyId, Name, EndpointUrl, EventsCsv, IsActive, CreatedAt, UpdatedAt
FROM WebhookSubscriptions
WHERE CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")}
ORDER BY Name, Id;
""", command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new Subscription(
                HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "Name"), HrmsDatabase.GetString(reader, "EndpointUrl"),
                HrmsDatabase.GetString(reader, "EventsCsv"), HrmsDatabase.GetBool(reader, "IsActive"),
                Convert.ToDateTime(reader["CreatedAt"]),
                reader["UpdatedAt"] == DBNull.Value ? null : Convert.ToDateTime(reader["UpdatedAt"])));
    }

    public static Task SaveSubscriptionAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int id, string name,
        Uri endpoint, string protectedSecret, string eventsCsv, bool isActive)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Webhook company is outside the effective scope.");
        if (!WebhookEndpointPolicy.IsAllowed(endpoint))
            throw new ArgumentException("Webhook endpoint must be a public HTTPS URL.", nameof(endpoint));

        if (id > 0)
            return HrmsDatabase.ExecuteAsync(db, $"""
UPDATE WebhookSubscriptions
SET Name=@Name, EndpointUrl=@Endpoint, ProtectedSecret=
    CASE WHEN @Secret=N'' THEN ProtectedSecret ELSE @Secret END,
    EventsCsv=@Events, IsActive=@Active, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")};
""", Parameters);

        return HrmsDatabase.ExecuteAsync(db, """
INSERT INTO WebhookSubscriptions
    (CompanyId, Name, EndpointUrl, ProtectedSecret, EventsCsv, IsActive, CreatedAt)
VALUES (@CompanyId, @Name, @Endpoint, @Secret, @Events, @Active, SYSUTCDATETIME());
""", Parameters);

        void Parameters(DbCommand command)
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Name", name.Trim());
            HrmsDatabase.AddParameter(command, "@Endpoint", endpoint.AbsoluteUri);
            HrmsDatabase.AddParameter(command, "@Secret", protectedSecret);
            HrmsDatabase.AddParameter(command, "@Events", eventsCsv);
            HrmsDatabase.AddParameter(command, "@Active", isActive ? 1 : 0);
        }
    }

    public static Task DeleteSubscriptionAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int id)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Webhook company is outside the effective scope.");
        return HrmsDatabase.ExecuteAsync(db, $"""
UPDATE WebhookSubscriptions SET IsActive=0, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            });
    }

    public static Task EnqueueAsync(
        ApplicationDbContext db, int companyId, string eventType, object payload,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0) throw new ArgumentOutOfRangeException(nameof(companyId));
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var json = JsonSerializer.Serialize(payload);

        return HrmsDatabase.ExecuteAsync(db, """
INSERT INTO WebhookDeliveries
    (SubscriptionId, CompanyId, EventType, PayloadJson, IdempotencyKey, Status, AttemptCount, NextAttemptAt, CreatedAt)
SELECT s.Id, s.CompanyId, @EventType, @Payload, @Idempotency, N'Pending', 0, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM WebhookSubscriptions s
WHERE s.CompanyId=@CompanyId AND s.IsActive=1
  AND (s.EventsCsv=N'*' OR N',' + s.EventsCsv + N',' LIKE N'%,' + @EventType + N',%')
  AND NOT EXISTS (
      SELECT 1 FROM WebhookDeliveries d
      WHERE d.SubscriptionId=s.Id AND d.IdempotencyKey=@Idempotency);
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@EventType", eventType.Trim());
            HrmsDatabase.AddParameter(command, "@Payload", json);
            HrmsDatabase.AddParameter(command, "@Idempotency", idempotencyKey.Trim());
        });
    }

    public static async Task<List<Delivery>> ClaimAsync(
        ApplicationDbContext db, int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await HrmsDatabase.QueryAsync(db, """
;WITH due AS
(
    SELECT TOP (@Batch) d.Id
    FROM WebhookDeliveries d WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE ((d.Status IN (N'Pending', N'Retry') AND d.NextAttemptAt <= SYSUTCDATETIME())
           OR (d.Status=N'Processing' AND d.LastAttemptAt < DATEADD(minute,-5,SYSUTCDATETIME())))
      AND d.AttemptCount < @MaxAttempts
    ORDER BY d.NextAttemptAt, d.Id
)
UPDATE d
SET Status=N'Processing', AttemptCount=AttemptCount+1, LastAttemptAt=SYSUTCDATETIME()
OUTPUT inserted.Id, inserted.SubscriptionId, inserted.CompanyId, s.EndpointUrl, s.ProtectedSecret,
       inserted.EventType, inserted.PayloadJson, inserted.IdempotencyKey, inserted.AttemptCount
FROM WebhookDeliveries d
INNER JOIN due ON due.Id=d.Id
INNER JOIN WebhookSubscriptions s ON s.Id=d.SubscriptionId
WHERE s.IsActive=1 AND s.CompanyId=d.CompanyId;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Batch", Math.Clamp(batchSize, 1, 200));
            HrmsDatabase.AddParameter(command, "@MaxAttempts", Math.Clamp(maxAttempts, 1, 20));
        }, Map);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    public static Task MarkSentAsync(ApplicationDbContext db, long id, int statusCode) =>
        HrmsDatabase.ExecuteAsync(db, """
UPDATE WebhookDeliveries
SET Status=N'Sent', SentAt=SYSUTCDATETIME(), LastStatusCode=@Code, LastError=NULL
WHERE Id=@Id AND Status=N'Processing';
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@Code", statusCode);
        });

    public static Task MarkRetryAsync(
        ApplicationDbContext db, long id, int attempt, int maxAttempts, int? statusCode, string error)
    {
        var terminal = attempt >= maxAttempts;
        var seconds = Math.Min(3600, 15 * (int)Math.Pow(2, Math.Min(attempt - 1, 8)));
        return HrmsDatabase.ExecuteAsync(db, """
UPDATE WebhookDeliveries
SET Status=@Status, NextAttemptAt=DATEADD(second,@Delay,SYSUTCDATETIME()),
    LastStatusCode=@Code, LastError=LEFT(@Error,1000)
WHERE Id=@Id AND Status=N'Processing';
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@Status", terminal ? "DeadLetter" : "Retry");
            HrmsDatabase.AddParameter(command, "@Delay", seconds);
            HrmsDatabase.AddParameter(command, "@Code", statusCode is null ? DBNull.Value : statusCode.Value);
            HrmsDatabase.AddParameter(command, "@Error", error);
        });
    }

    private static Delivery Map(DbDataReader reader) => new(
        Convert.ToInt64(reader["Id"]), HrmsDatabase.GetInt(reader, "SubscriptionId"),
        HrmsDatabase.GetInt(reader, "CompanyId"), HrmsDatabase.GetString(reader, "EndpointUrl"),
        HrmsDatabase.GetString(reader, "ProtectedSecret"), HrmsDatabase.GetString(reader, "EventType"),
        HrmsDatabase.GetString(reader, "PayloadJson"), HrmsDatabase.GetString(reader, "IdempotencyKey"),
        HrmsDatabase.GetInt(reader, "AttemptCount"));
}
