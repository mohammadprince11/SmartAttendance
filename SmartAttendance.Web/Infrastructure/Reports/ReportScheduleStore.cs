using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Reports;

public static class ReportScheduleStore
{
    public sealed record Schedule(
        int Id, int CompanyId, int ReportId, int OwnerUserId, string OwnerUser,
        string RecipientsCsv, string Frequency, int HourUtc, int? DayOfWeek,
        DateTime NextRunAt, bool IsActive, int AttemptCount, string? LastError);

    public static async Task<List<Schedule>> ListAsync(
        ApplicationDbContext db, CompanyScope scope, string ownerUser)
    {
        var predicate = scope.ToSqlPredicate("s.CompanyId");
        return await HrmsDatabase.QueryAsync(db, $"""
SELECT s.* FROM ReportSchedules s
WHERE s.OwnerUser=@Owner AND {predicate}
ORDER BY s.IsActive DESC,s.NextRunAt,s.Id;
""", command => HrmsDatabase.AddParameter(command, "@Owner", ownerUser), Map);
    }

    public static async Task CreateAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int reportId,
        int ownerUserId, string ownerUser, string recipientsCsv, string frequency,
        int hourUtc, int? dayOfWeek)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        var report = await PeopleReportsStore.GetAsync(db, scope, reportId);
        if (report is null || report.CompanyId != companyId || report.IsSystem ||
            !string.Equals(report.OwnerUser, ownerUser, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();

        frequency = frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" : "Daily";
        hourUtc = Math.Clamp(hourUtc, 0, 23);
        dayOfWeek = frequency == "Weekly" ? Math.Clamp(dayOfWeek ?? 0, 0, 6) : null;
        var next = Next(DateTime.UtcNow, frequency, hourUtc, dayOfWeek);
        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO ReportSchedules(CompanyId,ReportId,OwnerUserId,OwnerUser,RecipientsCsv,Frequency,HourUtc,DayOfWeek,NextRunAt,IsActive,AttemptCount,CreatedAt)
VALUES(@Company,@Report,@UserId,@Owner,@Recipients,@Frequency,@Hour,@Day,@Next,1,0,SYSUTCDATETIME());
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Company", companyId);
            HrmsDatabase.AddParameter(command, "@Report", reportId);
            HrmsDatabase.AddParameter(command, "@UserId", ownerUserId);
            HrmsDatabase.AddParameter(command, "@Owner", ownerUser);
            HrmsDatabase.AddParameter(command, "@Recipients", recipientsCsv);
            HrmsDatabase.AddParameter(command, "@Frequency", frequency);
            HrmsDatabase.AddParameter(command, "@Hour", hourUtc);
            HrmsDatabase.AddParameter(command, "@Day", dayOfWeek is null ? DBNull.Value : dayOfWeek);
            HrmsDatabase.AddParameter(command, "@Next", next);
        });
    }

    public static Task DisableAsync(ApplicationDbContext db, CompanyScope scope, int id, string ownerUser) =>
        HrmsDatabase.ExecuteAsync(db,
            $"UPDATE ReportSchedules SET IsActive=0 WHERE Id=@Id AND OwnerUser=@Owner AND {scope.ToSqlPredicate("CompanyId")};",
            command => { HrmsDatabase.AddParameter(command, "@Id", id); HrmsDatabase.AddParameter(command, "@Owner", ownerUser); });

    public static async Task<List<Schedule>> ClaimDueAsync(ApplicationDbContext db, int take)
    {
        return await HrmsDatabase.QueryAsync(db, """
;WITH due AS
(
 SELECT TOP (@Take) * FROM ReportSchedules WITH (UPDLOCK,READPAST,ROWLOCK)
 WHERE IsActive=1 AND NextRunAt<=SYSUTCDATETIME() AND (RetryAt IS NULL OR RetryAt<=SYSUTCDATETIME())
   AND (ProcessingAt IS NULL OR ProcessingAt<DATEADD(minute,-15,SYSUTCDATETIME()))
 ORDER BY NextRunAt,Id
)
UPDATE due SET ProcessingAt=SYSUTCDATETIME()
OUTPUT inserted.*;
""", command => HrmsDatabase.AddParameter(command, "@Take", Math.Clamp(take, 1, 100)), Map);
    }

    public static Task MarkAsync(ApplicationDbContext db, Schedule schedule, bool sent, string? error)
    {
        var next = Next(DateTime.UtcNow, schedule.Frequency, schedule.HourUtc, schedule.DayOfWeek);
        return HrmsDatabase.ExecuteAsync(db, """
UPDATE ReportSchedules SET ProcessingAt=NULL,LastRunAt=SYSUTCDATETIME(),LastSent=@Sent,
 LastError=@Error,AttemptCount=CASE WHEN @Sent=1 THEN 0 ELSE AttemptCount+1 END,
 NextRunAt=CASE WHEN @Sent=1 THEN @Next ELSE NextRunAt END,
 RetryAt=CASE WHEN @Sent=1 THEN NULL ELSE @Retry END,
 IsActive=CASE WHEN @Sent=0 AND AttemptCount>=7 THEN 0 ELSE IsActive END
WHERE Id=@Id;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", schedule.Id);
            HrmsDatabase.AddParameter(command, "@Sent", sent ? 1 : 0);
            HrmsDatabase.AddParameter(command, "@Error", string.IsNullOrWhiteSpace(error) ? DBNull.Value : error[..Math.Min(error.Length, 1000)]);
            HrmsDatabase.AddParameter(command, "@Next", next);
            HrmsDatabase.AddParameter(command, "@Retry", DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, schedule.AttemptCount + 1))));
        });
    }

    public static Task<int> DeliveryExistsAsync(ApplicationDbContext db, int scheduleId, DateTime occurrenceAt, string recipient) =>
        HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ReportScheduleDeliveries WHERE ScheduleId=@Id AND OccurrenceAt=@At AND Recipient=@Recipient AND SentAt IS NOT NULL;",
            command => { HrmsDatabase.AddParameter(command,"@Id",scheduleId); HrmsDatabase.AddParameter(command,"@At",occurrenceAt); HrmsDatabase.AddParameter(command,"@Recipient",recipient); });

    public static Task RecordDeliveryAsync(ApplicationDbContext db, int scheduleId, DateTime occurrenceAt, string recipient) =>
        HrmsDatabase.ExecuteAsync(db, """
IF NOT EXISTS(SELECT 1 FROM ReportScheduleDeliveries WHERE ScheduleId=@Id AND OccurrenceAt=@At AND Recipient=@Recipient)
 INSERT INTO ReportScheduleDeliveries(ScheduleId,OccurrenceAt,Recipient,SentAt) VALUES(@Id,@At,@Recipient,SYSUTCDATETIME());
ELSE
 UPDATE ReportScheduleDeliveries SET SentAt=SYSUTCDATETIME() WHERE ScheduleId=@Id AND OccurrenceAt=@At AND Recipient=@Recipient;
""", command => { HrmsDatabase.AddParameter(command,"@Id",scheduleId); HrmsDatabase.AddParameter(command,"@At",occurrenceAt); HrmsDatabase.AddParameter(command,"@Recipient",recipient); });

    public static DateTime Next(DateTime utcNow, string frequency, int hourUtc, int? dayOfWeek)
    {
        var candidate = utcNow.Date.AddHours(Math.Clamp(hourUtc, 0, 23));
        if (frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var wanted = Math.Clamp(dayOfWeek ?? 0, 0, 6);
            var days = (wanted - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(days);
            if (candidate <= utcNow) candidate = candidate.AddDays(7);
        }
        else if (candidate <= utcNow) candidate = candidate.AddDays(1);
        return DateTime.SpecifyKind(candidate, DateTimeKind.Utc);
    }

    private static Schedule Map(DbDataReader r) => new(
        HrmsDatabase.GetInt(r,"Id"), HrmsDatabase.GetInt(r,"CompanyId"), HrmsDatabase.GetInt(r,"ReportId"),
        HrmsDatabase.GetInt(r,"OwnerUserId"), HrmsDatabase.GetString(r,"OwnerUser"), HrmsDatabase.GetString(r,"RecipientsCsv"),
        HrmsDatabase.GetString(r,"Frequency"), HrmsDatabase.GetInt(r,"HourUtc"), r["DayOfWeek"]==DBNull.Value?null:HrmsDatabase.GetInt(r,"DayOfWeek"),
        HrmsDatabase.GetDateTime(r,"NextRunAt") ?? DateTime.MinValue, HrmsDatabase.GetBool(r,"IsActive"), HrmsDatabase.GetInt(r,"AttemptCount"),
        string.IsNullOrWhiteSpace(HrmsDatabase.GetString(r,"LastError"))?null:HrmsDatabase.GetString(r,"LastError"));
}
