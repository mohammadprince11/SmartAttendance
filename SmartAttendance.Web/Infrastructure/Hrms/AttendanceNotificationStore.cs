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

    /// <summary>قناة الإشعار — بريد إلكتروني أو إشعار داخل النظام/تطبيق الموبايل (لا SMS).</summary>
    public static readonly (string Key, string Label)[] Channels =
    {
        ("Email", "البريد الإلكتروني"),
        ("System", "إشعار النظام (تطبيق الموبايل/رابط الحساب)")
    };

    /// <summary>وضع «نسخة إلى» (CC): بلا / موظفون محددون / المدير المباشر / كلاهما.</summary>
    public static readonly (string Key, string Label)[] CcModes =
    {
        ("None", "بلا"),
        ("Specific", "موظفون محددون"),
        ("Manager", "المدير المباشر"),
        ("Both", "كلاهما")
    };

    public static string LabelOf(string key) => Types.FirstOrDefault(t => t.Key == key).Label ?? key;

    /// <summary>هل النوع أسبوعي (سنة/شهر/أسبوع) أم بمدى تاريخ (ملخص)؟</summary>
    public static bool IsWeekly(string type) => type is "WeeklyAbsence" or "WeeklyShortage";

    /// <summary>
    /// نص القالب الافتراضي لكل نوع — برموز مختلفة حسب النوع (مطابقة كيان):
    /// • ملخص: {0}غياب {1}تأخير {2}خروج مبكر {3}الفترة {4}بصمات مفقودة
    /// • غياب أسبوعي: {0}عدد الغياب {1}قائمة أيام الغياب {2}رقم الأسبوع {3}فترة الأسبوع {4}الشهر {5}السنة
    /// • نقص ساعات أسبوعي: {0}مرات النقص {1}الساعات المطلوبة {2}الساعات الفعلية {3}رقم الأسبوع {4}فترة الأسبوع {5}الشهر {6}السنة
    /// </summary>
    public static string DefaultTemplate(string type) => type switch
    {
        "WeeklyAbsence" =>
            "عزيزي الموظف/ة، خلال الأسبوع {2} ({3}) من شهر {4}/{5}: لديك {0} أيام غياب"
            + " ({1}). يُرجى مراجعة حضورك قبل اعتماد الحركات.",
        "WeeklyShortage" =>
            "عزيزي الموظف/ة، خلال الأسبوع {3} ({4}) من شهر {5}/{6}: لديك {0} مرات نقص ساعات؛"
            + " المطلوب {1} ساعة والفعلي {2} ساعة. يُرجى الالتزام بأوقات المناوبة.",
        _ =>
            "عزيزي الموظف/ة، يُرجى العلم أن لديك خلال الفترة ({3}): {0} أيام غياب، {1} ساعات تأخير، "
            + "{2} ساعات خروج مبكر، و{4} بصمات مفقودة. يُرجى مراجعة دوامك قبل اعتماد الحركات."
    };

    /// <summary>عدد أسابيع الشهر (أسابيع بطول 7 من اليوم 1) وحدود أسبوع محدد.</summary>
    public static int WeeksInMonth(int year, int month) =>
        (int)Math.Ceiling(DateTime.DaysInMonth(year, month) / 7.0);

    public static (DateOnly From, DateOnly To) WeekRange(int year, int month, int week)
    {
        var days = DateTime.DaysInMonth(year, month);
        var startDay = Math.Clamp((week - 1) * 7 + 1, 1, days);
        var endDay = Math.Min(week * 7, days);
        return (new DateOnly(year, month, startDay), new DateOnly(year, month, endDay));
    }

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
IF COL_LENGTH('AttendanceNotifications','CcMode') IS NULL ALTER TABLE AttendanceNotifications ADD CcMode nvarchar(20) NOT NULL CONSTRAINT DF_AN_Cc DEFAULT(N'None');
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
    private static readonly string[] MonthNames =
    {
        "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    };

    public static async Task<(int NotificationId, int Recipients, int CcCount)> SendAsync(
        ApplicationDbContext dbContext, string type, DateOnly from, DateOnly to, int week,
        string channel, string template, IReadOnlyCollection<int> employeeIds,
        string ccMode, IReadOnlyCollection<int> ccEmployeeIds)
    {
        await EnsureAsync(dbContext);
        if (to < from) (from, to) = (to, from);
        if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate(type);

        // يوميات الفترة لكل موظف (لحساب رموز كل نوع — منها قائمة أيام الغياب)
        var rowsByEmp = (await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, WorkDate, Status, LateHours, EarlyLeaveHours, WorkedHours
FROM DayAttendances
WHERE WorkDate >= @From AND WorkDate <= @To
ORDER BY EmployeeId, WorkDate;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new DayStat(
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                HrmsDatabase.GetString(reader, "Status"),
                reader["LateHours"] is decimal l ? l : 0,
                reader["EarlyLeaveHours"] is decimal e ? e : 0,
                reader["WorkedHours"] is decimal w ? w : 0)))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var period = $"{from:yyyy-MM-dd} → {to:yyyy-MM-dd}";

        var notificationId = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO AttendanceNotifications (NotifType, FromDate, ToDate, Channel, MessageTemplate, CcMode, Recipients)
VALUES (@Type, @From, @To, @Channel, @Template, @CcMode, @Recipients);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Type", type);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Channel", channel);
                HrmsDatabase.AddParameter(command, "@Template", template);
                HrmsDatabase.AddParameter(command, "@CcMode", string.IsNullOrWhiteSpace(ccMode) ? "None" : ccMode);
                HrmsDatabase.AddParameter(command, "@Recipients", employeeIds.Count);
            });

        // خرائط المدير المباشر (لـ CC «المدير المباشر») — للأهداف فقط
        var managerOf = new Dictionary<int, int>();
        if (ccMode is "Manager" or "Both")
        {
            managerOf = (await HrmsDatabase.QueryAsync(
                dbContext,
                "SELECT Id, DirectManagerId FROM Employees WHERE DirectManagerId IS NOT NULL;",
                command => { },
                reader => new { Id = HrmsDatabase.GetInt(reader, "Id"), Mgr = HrmsDatabase.GetNullableInt(reader, "DirectManagerId") }))
                .Where(x => x.Mgr is int).ToDictionary(x => x.Id, x => x.Mgr!.Value);
        }

        int cc = 0;
        foreach (var empId in employeeIds)
        {
            rowsByEmp.TryGetValue(empId, out var rows);
            var message = Render(type, template, rows ?? new(), from, to, week, period);
            await AddOutboxAsync(dbContext, notificationId, empId, channel, message);

            // نسخة للمدير المباشر (نسخة من رسالة كل موظف)
            if ((ccMode is "Manager" or "Both") && managerOf.TryGetValue(empId, out var mgr))
            {
                await AddOutboxAsync(dbContext, notificationId, mgr, channel, "نسخة (المدير المباشر): " + message);
                cc++;
            }
        }

        // نسخة لموظفين محددين (رسالة ملخص واحدة لكل منهم)
        if ((ccMode is "Specific" or "Both") && ccEmployeeIds.Count > 0)
        {
            var summary = $"نسخة: تم إشعار {employeeIds.Count} موظفاً بـ«{LabelOf(type)}» للفترة ({period}).";
            foreach (var ccId in ccEmployeeIds)
            {
                await AddOutboxAsync(dbContext, notificationId, ccId, channel, summary);
                cc++;
            }
        }

        return (notificationId, employeeIds.Count, cc);
    }

    private sealed record DayStat(int EmployeeId, DateOnly Date, string Status,
        decimal Late, decimal Early, decimal Worked);

    /// <summary>يولّد رسالة موظف حسب النوع، بملء الرموز من يومياته بالفترة.</summary>
    private static string Render(string type, string template, List<DayStat> rows,
        DateOnly from, DateOnly to, int week, string period)
    {
        var monthName = MonthNames[Math.Clamp(from.Month, 1, 12)];
        switch (type)
        {
            case "WeeklyAbsence":
            {
                var absent = rows.Where(r => r.Status == "Absent").Select(r => r.Date).ToList();
                var list = absent.Count > 0 ? string.Join("، ", absent.Select(d => d.ToString("MM-dd"))) : "لا شيء";
                return template
                    .Replace("{0}", absent.Count.ToString())
                    .Replace("{1}", list)
                    .Replace("{2}", week.ToString())
                    .Replace("{3}", period)
                    .Replace("{4}", monthName)
                    .Replace("{5}", from.Year.ToString());
            }
            case "WeeklyShortage":
            {
                var shortDays = rows.Count(r => r.Late > 0 || r.Early > 0);
                var actual = rows.Sum(r => r.Worked);
                var required = rows.Sum(r => r.Worked + r.Late + r.Early);   // مطلوب ≈ فعلي + نقص
                return template
                    .Replace("{0}", shortDays.ToString())
                    .Replace("{1}", required.ToString("0.##"))
                    .Replace("{2}", actual.ToString("0.##"))
                    .Replace("{3}", week.ToString())
                    .Replace("{4}", period)
                    .Replace("{5}", monthName)
                    .Replace("{6}", from.Year.ToString());
            }
            default: // Summary
            {
                var absence = rows.Count(r => r.Status == "Absent");
                var late = rows.Sum(r => r.Late);
                var early = rows.Sum(r => r.Early);
                var missing = rows.Count(r => r.Status == "Incomplete");
                return template
                    .Replace("{0}", absence.ToString())
                    .Replace("{1}", late.ToString("0.##"))
                    .Replace("{2}", early.ToString("0.##"))
                    .Replace("{3}", period)
                    .Replace("{4}", missing.ToString());
            }
        }
    }

    private static Task AddOutboxAsync(ApplicationDbContext dbContext, int notificationId,
        int employeeId, string channel, string message) =>
        HrmsDatabase.ExecuteAsync(
            dbContext,
            "INSERT INTO AttendanceNotificationOutbox (NotificationId, EmployeeId, Channel, RenderedMessage) VALUES (@N, @E, @C, @M);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@N", notificationId);
                HrmsDatabase.AddParameter(command, "@E", employeeId);
                HrmsDatabase.AddParameter(command, "@C", channel);
                HrmsDatabase.AddParameter(command, "@M", message.Length > 1200 ? message[..1200] : message);
            });
}
