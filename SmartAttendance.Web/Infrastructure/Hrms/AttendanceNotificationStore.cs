using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// إشعارات الحضور (نمط كيان — «إشعار الموظف» بمستعرض الحضور): تسجّل نية إبلاغ
/// الموظفين بملخص الحضور/الغياب الأسبوعي/نقص الساعات لشهر. التسليم الفعلي عبر قناة
/// الإشعارات لاحقاً؛ هذا المخزن يسجّل الطلب ويربطه بالشهر ونطاق الموظفين.
/// </summary>
public static class AttendanceNotificationStore
{
    public static readonly (string Key, string Label)[] Types =
    {
        ("Summary", "ملخص الحضور"),
        ("WeeklyAbsence", "الغياب الأسبوعي"),
        ("WeeklyShortage", "نقص الساعات الأسبوعي")
    };

    public static string LabelOf(string key) => Types.FirstOrDefault(t => t.Key == key).Label ?? key;

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
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        Recipients int NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");
    }

    public static async Task<int> CreateAsync(ApplicationDbContext dbContext,
        string notifType, int year, int month, int recipients)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO AttendanceNotifications (NotifType, [Year], [Month], Recipients)
VALUES (@Type, @Y, @M, @R);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Type", notifType);
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@R", recipients);
            });
    }
}
