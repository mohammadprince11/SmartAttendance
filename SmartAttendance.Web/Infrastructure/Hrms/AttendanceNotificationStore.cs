using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// إشعارات الحضور (نمط كيان — «إشعار الموظف» بمستعرض الحضور): مؤلّف رسالة لإبلاغ
/// الموظفين بملخص حضورهم لفترة عبر قناة (بريد/SMS)، بنص قالب يحوي رموزاً تُملأ من
/// حضور كل موظف الفعلي: (0) أيام الغياب · (1) ساعات التأخير · (2) ساعات الخروج المبكر
/// · (3) الفترة · (4) البصمات المفقودة. تُخزَّن الرسالة المولّدة لكل موظف بصندوق صادر
/// (التسليم الفعلي عبر قناة البريد/الرسائل لاحقاً).
/// </summary>
public static class AttendanceNotificationStore
{
    public static readonly (string Key, string Label)[] Types =
    {
        ("Summary", "ملخص الحضور"),
        ("WeeklyAbsence", "الغياب الأسبوعي"),
        ("WeeklyShortage", "نقص الساعات الأسبوعي")
    };

    public static readonly (string Key, string Label)[] Channels =
    {
        ("Email", "البريد الإلكتروني"),
        ("Sms", "رسالة نصية")
    };

    public static string LabelOf(string key) => Types.FirstOrDefault(t => t.Key == key).Label ?? key;

    /// <summary>نص القالب الافتراضي لكل نوع — يحوي الرموز {0}..{4}.</summary>
    public static string DefaultTemplate(string type) => type switch
    {
        "WeeklyAbsence" =>
            "عزيزي الموظف/ة، لديك {0} أيام غياب خلال الفترة ({3}). يُرجى مراجعة حضورك قبل اعتماد الحركات.",
        "WeeklyShortage" =>
            "عزيزي الموظف/ة، لديك {1} ساعات تأخير و{2} ساعات خروج مبكر خلال الفترة ({3}). يُرجى الالتزام بأوقات المناوبة.",
        _ =>
            "عزيزي الموظف/ة، يُرجى العلم أن لديك خلال الفترة ({3}): {0} أيام غياب، {1} ساعات تأخير، "
            + "{2} ساعات خروج مبكر، و{4} بصمات مفقودة. يُرجى مراجعة دوامك قبل اعتماد الحركات."
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AttendanceNotifications', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceNotifications
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NotifType nvarchar(30) NOT NULL,
        FromDate date NOT NULL,
        ToDate date NOT NULL,
        Channel nvarchar(10) NOT NULL DEFAULT(N'Email'),
        MessageTemplate nvarchar(1000) NOT NULL DEFAULT(N''),
        Recipients int NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- ترقية من المخطط القديم (Year/Month) للمؤلّف الكامل (فترة/قناة/نص) — idempotent
IF COL_LENGTH('AttendanceNotifications','FromDate') IS NULL ALTER TABLE AttendanceNotifications ADD FromDate date NOT NULL CONSTRAINT DF_AN_From DEFAULT('2000-01-01');
IF COL_LENGTH('AttendanceNotifications','ToDate') IS NULL ALTER TABLE AttendanceNotifications ADD ToDate date NOT NULL CONSTRAINT DF_AN_To DEFAULT('2000-01-01');
IF COL_LENGTH('AttendanceNotifications','Channel') IS NULL ALTER TABLE AttendanceNotifications ADD Channel nvarchar(10) NOT NULL CONSTRAINT DF_AN_Ch DEFAULT(N'Email');
IF COL_LENGTH('AttendanceNotifications','MessageTemplate') IS NULL ALTER TABLE AttendanceNotifications ADD MessageTemplate nvarchar(1000) NOT NULL CONSTRAINT DF_AN_Msg DEFAULT(N'');
IF COL_LENGTH('AttendanceNotifications','Year') IS NOT NULL ALTER TABLE AttendanceNotifications DROP COLUMN [Year];
IF COL_LENGTH('AttendanceNotifications','Month') IS NOT NULL ALTER TABLE AttendanceNotifications DROP COLUMN [Month];

IF OBJECT_ID('AttendanceNotificationOutbox', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceNotificationOutbox
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NotificationId int NOT NULL,
        EmployeeId int NOT NULL,
        Channel nvarchar(10) NOT NULL,
        RenderedMessage nvarchar(1200) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_AttNotifOutbox_Notification ON AttendanceNotificationOutbox (NotificationId);
END;
""");
    }

    /// <summary>
    /// يؤلّف ويخزّن إشعاراً: يحسب رموز حضور كل موظف بالفترة، يولّد رسالته، ويكتبها
    /// بصندوق الصادر. يعيد (معرّف الإشعار، عدد المستلمين).
    /// </summary>
    public static async Task<(int NotificationId, int Recipients)> SendAsync(
        ApplicationDbContext dbContext, string type, DateOnly from, DateOnly to,
        string channel, string template, IReadOnlyCollection<int> employeeIds)
    {
        await EnsureAsync(dbContext);
        if (to < from) (from, to) = (to, from);
        if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate(type);

        // مجاميع الحضور لكل موظف بالفترة (غياب/تأخير/خروج مبكر/بصمات مفقودة)
        var stats = (await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId,
       SUM(CASE WHEN Status = N'Absent' THEN 1 ELSE 0 END) AS AbsenceDays,
       SUM(LateHours) AS LateHours,
       SUM(EarlyLeaveHours) AS EarlyHours,
       SUM(CASE WHEN Status = N'Incomplete' THEN 1 ELSE 0 END) AS Missing
FROM DayAttendances
WHERE WorkDate >= @From AND WorkDate <= @To
GROUP BY EmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                AbsenceDays = HrmsDatabase.GetInt(reader, "AbsenceDays"),
                LateHours = reader["LateHours"] is decimal l ? l : 0,
                EarlyHours = reader["EarlyHours"] is decimal e ? e : 0,
                Missing = HrmsDatabase.GetInt(reader, "Missing")
            })).ToDictionary(x => x.EmployeeId);

        var period = $"{from:yyyy-MM-dd} → {to:yyyy-MM-dd}";

        var notificationId = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO AttendanceNotifications (NotifType, FromDate, ToDate, Channel, MessageTemplate, Recipients)
VALUES (@Type, @From, @To, @Channel, @Template, @Recipients);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Type", type);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Channel", channel);
                HrmsDatabase.AddParameter(command, "@Template", template);
                HrmsDatabase.AddParameter(command, "@Recipients", employeeIds.Count);
            });

        foreach (var empId in employeeIds)
        {
            stats.TryGetValue(empId, out var s);
            var message = template
                .Replace("{0}", (s?.AbsenceDays ?? 0).ToString())
                .Replace("{1}", (s?.LateHours ?? 0).ToString("0.##"))
                .Replace("{2}", (s?.EarlyHours ?? 0).ToString("0.##"))
                .Replace("{3}", period)
                .Replace("{4}", (s?.Missing ?? 0).ToString());
            var id = empId;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO AttendanceNotificationOutbox (NotificationId, EmployeeId, Channel, RenderedMessage) VALUES (@N, @E, @C, @M);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@N", notificationId);
                    HrmsDatabase.AddParameter(command, "@E", id);
                    HrmsDatabase.AddParameter(command, "@C", channel);
                    HrmsDatabase.AddParameter(command, "@M", message.Length > 1200 ? message[..1200] : message);
                });
        }

        return (notificationId, employeeIds.Count);
    }
}
