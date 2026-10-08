using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>Durable per-company email intents committed with the notification event.</summary>
public static class NotificationRuleMailOutbox
{
    public const string MigrationSql = """
IF OBJECT_ID('ZynoraNotificationMailOutbox', 'U') IS NULL
BEGIN
    CREATE TABLE ZynoraNotificationMailOutbox
    (
        Id bigint IDENTITY NOT NULL PRIMARY KEY,
        EventKey nvarchar(200) NOT NULL,
        CompanyId int NOT NULL,
        EmployeeId int NOT NULL,
        Subject nvarchar(200) NOT NULL,
        Body nvarchar(4000) NOT NULL,
        Status nvarchar(16) NOT NULL CONSTRAINT DF_ZNMO_Status DEFAULT(N'Pending'),
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_ZNMO_Created DEFAULT(SYSUTCDATETIME()),
        AttemptedAtUtc datetime2 NULL,
        SentAtUtc datetime2 NULL,
        CONSTRAINT UQ_ZNMO_EventRecipient UNIQUE(EventKey, CompanyId, EmployeeId),
        CONSTRAINT FK_ZNMO_Company FOREIGN KEY(CompanyId) REFERENCES Companies(Id),
        CONSTRAINT FK_ZNMO_Employee FOREIGN KEY(EmployeeId) REFERENCES Employees(Id),
        CONSTRAINT CK_ZNMO_Status CHECK(Status IN (N'Pending', N'Sending', N'Sent', N'Failed', N'NoAddress', N'Excluded'))
    );
    CREATE INDEX IX_ZNMO_Pending ON ZynoraNotificationMailOutbox(Status, Id);
END;
""";

    public static Task EnqueueAsync(ApplicationDbContext db, string eventKey, int companyId, int employeeId, string subject, string body) =>
        HrmsDatabase.ExecuteAsync(db, """
INSERT INTO ZynoraNotificationMailOutbox(EventKey, CompanyId, EmployeeId, Subject, Body)
SELECT @Key, @CompanyId, e.Id, @Subject, @Body FROM Employees e
WHERE e.Id = @EmployeeId AND e.CompanyId = @CompanyId AND e.IsActive = 1 AND e.IsDeleted = 0;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Key", eventKey);
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            HrmsDatabase.AddParameter(command, "@Subject", subject);
            HrmsDatabase.AddParameter(command, "@Body", body);
        });

    // Caller holds ZYNORA.NotificationDispatcher lock across processes.
    // SMTP cannot guarantee exactly-once: an ambiguous attempt remains Sending, never auto-replayed.
    public static async Task DispatchAsync(ApplicationDbContext db, IEmailSender sender, int batchSize, CancellationToken token)
    {
        if (!sender.IsEnabled) return;
        // Upgrading the app before applying the controlled migration must not stop
        // the pre-existing attendance dispatcher. This check never creates schema.
        if (await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT CASE WHEN OBJECT_ID('ZynoraNotificationMailOutbox', 'U') IS NOT NULL THEN 1 ELSE 0 END;", _ => { }) != 1) return;
        var rows = await HrmsDatabase.QueryAsync(db, """
SELECT TOP (@Batch) o.Id, o.Subject, o.Body,
    CASE WHEN e.Id IS NOT NULL AND c.Id IS NOT NULL THEN 1 ELSE 0 END AS Eligible,
    ISNULL(e.Email, N'') AS Email, ISNULL(e.PersonalEmail, N'') AS PersonalEmail
FROM ZynoraNotificationMailOutbox o
LEFT JOIN Employees e ON e.Id = o.EmployeeId AND e.CompanyId = o.CompanyId AND e.IsActive = 1 AND e.IsDeleted = 0
LEFT JOIN Companies c ON c.Id = o.CompanyId AND c.IsActive = 1 AND c.IsDeleted = 0
WHERE o.Status = N'Pending' ORDER BY o.Id;
""", command => HrmsDatabase.AddParameter(command, "@Batch", Math.Clamp(batchSize, 1, 100)),
            reader => (Id: Convert.ToInt64(reader["Id"]), Subject: HrmsDatabase.GetString(reader, "Subject"),
                Body: HrmsDatabase.GetString(reader, "Body"), Eligible: HrmsDatabase.GetInt(reader, "Eligible") == 1,
                Email: NotificationDispatcher.ResolveEmail(HrmsDatabase.GetString(reader, "Email"), HrmsDatabase.GetString(reader, "PersonalEmail"))));
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (!row.Eligible) { await MarkAsync(db, row.Id, "Excluded"); continue; }
            if (string.IsNullOrWhiteSpace(row.Email)) { await MarkAsync(db, row.Id, "NoAddress"); continue; }
            await MarkAsync(db, row.Id, "Sending");
            var result = await sender.SendAsync(new EmailMessage(row.Email, row.Subject, row.Body), token);
            await MarkAsync(db, row.Id, result.Sent ? "Sent" : "Failed");
        }
    }

    private static Task MarkAsync(ApplicationDbContext db, long id, string status) => HrmsDatabase.ExecuteAsync(db, """
UPDATE ZynoraNotificationMailOutbox SET Status = @Status,
    AttemptedAtUtc = CASE WHEN @Status = N'Sending' THEN SYSUTCDATETIME() ELSE AttemptedAtUtc END,
    SentAtUtc = CASE WHEN @Status = N'Sent' THEN SYSUTCDATETIME() ELSE SentAtUtc END
WHERE Id = @Id;
""", command => { HrmsDatabase.AddParameter(command, "@Id", id); HrmsDatabase.AddParameter(command, "@Status", status); });
}
