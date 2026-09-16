using SmartAttendance.Infrastructure.Persistence;
using System.Text;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تسليم قسائم الرواتب بالبريد مع سجل idempotent لكل موظف داخل الدفعة.
/// لا تتحول الدفعة إلى PayslipSent إلا بعد نجاح كل القسائم.
/// </summary>
public static class PayrollPayslipDeliveryStore
{
    public static Task EnsureAsync(ApplicationDbContext db) => HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('PayrollPayslipDeliveries','U') IS NULL
BEGIN
    CREATE TABLE PayrollPayslipDeliveries
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId int NOT NULL,
        EmployeeId int NOT NULL,
        Email nvarchar(320) NULL,
        Status nvarchar(20) NOT NULL DEFAULT(N'Pending'),
        Attempts int NOT NULL DEFAULT(0),
        LastAttemptAt datetime2 NULL,
        SentAt datetime2 NULL,
        Error nvarchar(500) NULL
    );
    CREATE UNIQUE INDEX UX_PayrollPayslipDeliveries_RunEmployee
        ON PayrollPayslipDeliveries(RunId,EmployeeId);
END;
""");
    public static async Task<(bool Ok, string Message)> SendRunAsync(
        ApplicationDbContext db, int runId, IEmailSender sender,
        CancellationToken cancellationToken = default)
    {
        await EnsureAsync(db);
        if (!sender.IsEnabled)
            return (false, "قناة البريد غير مفعّلة أو ناقصة الإعداد — لم تُغيّر حالة الدفعة.");

        var run = await PayrollRunStore.GetRunAsync(db, runId);
        if (run == null) return (false, "الدفعة غير موجودة.");
        if (run.Status == "PayslipSent") return (true, "القسائم مُرسلة مسبقاً.");
        if (run.Status != "Issued")
            return (false, "يجب إصدار الدفعة قبل إرسال القسائم.");

        var lines = await PayrollRunStore.ListLinesAsync(db, runId);
        if (lines.Count == 0) return (false, "لا توجد قسائم داخل الدفعة.");

        var emails = await LoadEmailsAsync(db, lines.Select(x => x.EmployeeId).ToArray());
        var sent = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var email = emails.GetValueOrDefault(line.EmployeeId) ?? string.Empty;
            var claim = await ClaimAsync(db, runId, line.EmployeeId, email);
            if (!claim)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                await MarkAsync(db, runId, line.EmployeeId, "Failed", "لا يوجد بريد للموظف.");
                failed++;
                continue;
            }

            var subject = $"قسيمة راتب {run.Month:00}/{run.Year} - {line.EmployeeName}";
            var body = BuildBody(run, line);
            var result = await sender.SendAsync(new EmailMessage(email, subject, body), cancellationToken);
            if (result.Sent)
            {
                await MarkAsync(db, runId, line.EmployeeId, "Sent", null);
                sent++;
            }
            else
            {
                await MarkAsync(db, runId, line.EmployeeId, "Failed", result.Error);
                failed++;
            }
        }

        var remaining = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(*)
FROM PayrollRunLines l
LEFT JOIN PayrollPayslipDeliveries d
  ON d.RunId=l.RunId AND d.EmployeeId=l.EmployeeId AND d.Status=N'Sent'
WHERE l.RunId=@Run AND d.Id IS NULL;
""", c => HrmsDatabase.AddParameter(c, "@Run", runId));

        if (remaining > 0)
            return (false, $"أُرسلت {sent} قسيمة، وتعذّر {failed}، والمتبقي {remaining}. يمكن إعادة المحاولة بدون تكرار القسائم الناجحة.");

        var changed = await HrmsDatabase.ScalarAsync<int>(db, """
UPDATE PayrollRuns
SET Status=N'PayslipSent', PayslipSentAt=SYSUTCDATETIME()
WHERE Id=@Run AND Status=N'Issued';
SELECT @@ROWCOUNT;
""", c => HrmsDatabase.AddParameter(c, "@Run", runId));

        if (changed == 0)
        {
            var latest = await PayrollRunStore.GetRunAsync(db, runId);
            if (latest?.Status != "PayslipSent")
                return (false, "تم إرسال القسائم لكن تعذّر إغلاق حالة الدفعة بسبب تغير حالتها بالتزامن.");
        }

        return (true, $"تم إرسال جميع القسائم بنجاح. أُرسل الآن {sent} وتجاوز النظام {skipped} قسيمة سبق إرسالها.");
    }

    private static async Task<Dictionary<int, string>> LoadEmailsAsync(
        ApplicationDbContext db, IReadOnlyCollection<int> employeeIds)
    {
        if (employeeIds.Count == 0) return new();
        var ids = employeeIds.Distinct().ToArray();
        var result = new Dictionary<int, string>();
        foreach (var chunk in ids.Chunk(400))
        {
            var args = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var rows = await HrmsDatabase.QueryAsync(db,
                $"SELECT Id,ISNULL(Email,N'') Email,ISNULL(PersonalEmail,N'') PersonalEmail FROM Employees WHERE Id IN ({args}) AND ISNULL(IsDeleted,0)=0;",
                c => { for (var i = 0; i < chunk.Length; i++) HrmsDatabase.AddParameter(c, $"@P{i}", chunk[i]); },
                r => new { Id = HrmsDatabase.GetInt(r, "Id"), Work = HrmsDatabase.GetString(r, "Email"), Personal = HrmsDatabase.GetString(r, "PersonalEmail") });
            foreach (var row in rows)
                result[row.Id] = !string.IsNullOrWhiteSpace(row.Work) ? row.Work.Trim() : row.Personal.Trim();
        }
        return result;
    }

    private static async Task<bool> ClaimAsync(ApplicationDbContext db, int runId, int employeeId, string email)
    {
        await HrmsDatabase.ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM PayrollPayslipDeliveries WHERE RunId=@Run AND EmployeeId=@Emp)
    INSERT INTO PayrollPayslipDeliveries(RunId,EmployeeId,Email,Status)
    VALUES(@Run,@Emp,@Email,N'Pending');
""", c =>
        {
            HrmsDatabase.AddParameter(c, "@Run", runId);
            HrmsDatabase.AddParameter(c, "@Emp", employeeId);
            HrmsDatabase.AddParameter(c, "@Email", (object?)email ?? DBNull.Value);
        });

        var claimed = await HrmsDatabase.ScalarAsync<int>(db, """
UPDATE PayrollPayslipDeliveries
SET Status=N'Sending', Email=@Email, Attempts=Attempts+1,
    LastAttemptAt=SYSUTCDATETIME(), Error=NULL
WHERE RunId=@Run AND EmployeeId=@Emp
  AND (Status IN (N'Pending',N'Failed')
       OR (Status=N'Sending' AND LastAttemptAt < DATEADD(minute,-10,SYSUTCDATETIME())));
SELECT @@ROWCOUNT;
""", c =>
        {
            HrmsDatabase.AddParameter(c, "@Run", runId);
            HrmsDatabase.AddParameter(c, "@Emp", employeeId);
            HrmsDatabase.AddParameter(c, "@Email", (object?)email ?? DBNull.Value);
        });
        return claimed == 1;
    }

    private static Task MarkAsync(
        ApplicationDbContext db, int runId, int employeeId, string status, string? error) =>
        HrmsDatabase.ExecuteAsync(db, """
UPDATE PayrollPayslipDeliveries
SET Status=@Status,
    SentAt=CASE WHEN @Status=N'Sent' THEN SYSUTCDATETIME() ELSE SentAt END,
    Error=@Error
WHERE RunId=@Run AND EmployeeId=@Emp;
""", c =>
        {
            HrmsDatabase.AddParameter(c, "@Status", status);
            HrmsDatabase.AddParameter(c, "@Error", (object?)(error is { Length: > 500 } ? error[..500] : error) ?? DBNull.Value);
            HrmsDatabase.AddParameter(c, "@Run", runId);
            HrmsDatabase.AddParameter(c, "@Emp", employeeId);
        });

    public static string BuildBody(PayrollRunStore.PayrollRun run, PayrollRunStore.PayrollLine line)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"قسيمة راتب {run.Month:00}/{run.Year}");
        sb.AppendLine($"الموظف: {line.EmployeeName} ({line.EmployeeNo})");
        sb.AppendLine($"الدفعة: {run.BatchNoText}");
        sb.AppendLine(new string('-', 40));
        foreach (var item in line.Earnings)
            sb.AppendLine($"+ {item.ItemName}: {item.Amount:#,0.00}");
        foreach (var item in line.Deductions)
            sb.AppendLine($"- {item.ItemName}: {item.Amount:#,0.00}");
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"الإجمالي: {line.GrossSalary:#,0.00}");
        sb.AppendLine($"الضريبة: {line.TaxAmount:#,0.00}");
        sb.AppendLine($"الضمان - حصة الموظف: {line.GosiEmployee:#,0.00}");
        sb.AppendLine($"خصومات أخرى: {line.OtherDeductions:#,0.00}");
        sb.AppendLine($"صافي الراتب: {line.NetSalary:#,0.00} {line.PayrollCurrency}".TrimEnd());
        sb.AppendLine();
        sb.AppendLine("هذه القسيمة صادرة من نظام ZYNORA HR.");
        return sb.ToString();
    }
}
