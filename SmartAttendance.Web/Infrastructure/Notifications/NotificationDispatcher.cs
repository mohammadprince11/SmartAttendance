using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// يسحب رسائل قناة البريد المعلّقة من صندوق صادر الإشعارات ويسلّمها عبر
/// <see cref="IEmailSender"/>، ثم يسِم كل صف Sent أو Failed. آمن لإعادة التشغيل:
/// يعالج صفوف Status='Pending' فقط ويستبعد المُرسَلة، فلا تكرار. صفوف القناة System
/// تبقى Pending ويتجاهلها (صندوق داخل التطبيق، لا بريد).
/// </summary>
public static class NotificationDispatcher
{
    /// <summary>
    /// يعالج دفعة من الصادر. يعيد (المُرسَل، الفاشل). إن كانت القناة معطّلة لا يفعل شيئاً.
    /// </summary>
    public static async Task<(int Sent, int Failed)> DispatchPendingAsync(
        ApplicationDbContext dbContext, IEmailSender sender, int batchSize, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        if (!sender.IsEnabled) return (0, 0);
        await AttendanceNotificationStore.EnsureAsync(dbContext);

        var pending = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT TOP (@Batch) o.Id, o.EmployeeId, o.Subject, o.RenderedMessage, o.Attempts,
       ISNULL(e.Email, N'') AS WorkEmail, ISNULL(e.PersonalEmail, N'') AS PersonalEmail
FROM AttendanceNotificationOutbox o
LEFT JOIN Employees e ON e.Id = o.EmployeeId
WHERE o.Channel = N'Email' AND o.Status = N'Pending'
ORDER BY o.Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Batch", batchSize),
            reader => new PendingRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Subject"),
                HrmsDatabase.GetString(reader, "RenderedMessage"),
                HrmsDatabase.GetInt(reader, "Attempts"),
                ResolveEmail(HrmsDatabase.GetString(reader, "WorkEmail"), HrmsDatabase.GetString(reader, "PersonalEmail"))));

        int sent = 0, failed = 0;
        foreach (var row in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(row.Email))
            {
                await MarkAsync(dbContext, row.Id, "Failed", row.Attempts + 1, "لا عنوان بريد للموظف.");
                failed++;
                continue;
            }

            var result = await sender.SendAsync(
                new EmailMessage(row.Email, row.Subject, row.Message), cancellationToken);

            if (result.Sent)
            {
                await MarkAsync(dbContext, row.Id, "Sent", row.Attempts + 1, null);
                sent++;
            }
            else
            {
                // يبقى Pending لإعادة المحاولة حتى استنفاد المحاولات، ثم يُوسَم Failed.
                var attempts = row.Attempts + 1;
                var status = attempts >= maxAttempts ? "Failed" : "Pending";
                await MarkAsync(dbContext, row.Id, status, attempts, result.Error);
                if (status == "Failed") failed++;
            }
        }

        return (sent, failed);
    }

    /// <summary>أولوية العنوان: بريد العمل ثم البريد الشخصي. منطق نقي قابل للاختبار.</summary>
    public static string ResolveEmail(string? workEmail, string? personalEmail)
    {
        if (!string.IsNullOrWhiteSpace(workEmail)) return workEmail.Trim();
        if (!string.IsNullOrWhiteSpace(personalEmail)) return personalEmail.Trim();
        return string.Empty;
    }

    private static Task MarkAsync(ApplicationDbContext dbContext, int id, string status, int attempts, string? error) =>
        HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AttendanceNotificationOutbox
SET Status = @Status, Attempts = @Attempts, Error = @Error,
    SentAt = CASE WHEN @Status = N'Sent' THEN SYSUTCDATETIME() ELSE SentAt END
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@Attempts", attempts);
                HrmsDatabase.AddParameter(command, "@Error", (object?)(error is { Length: > 400 } ? error[..400] : error) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Id", id);
            });

    private sealed record PendingRow(int Id, string Subject, string Message, int Attempts, string Email);
}
