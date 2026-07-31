using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Pages.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// مولّد مركز الإشعارات: يقرأ قواعد <c>NexoraNotificationRules</c> المفعّلة يومياً،
/// يطابقها بموظفي الشركة (عيد ميلاد · ذكرى عمل · قرب انتهاء العقد · قرب انتهاء فترة
/// التجربة)، ويُطلق إشعاراً فعلياً لكل حدث جديد: صندوق داخل النظام (<see cref="UserNotification"/>)
/// لكل مستلم + دفع Web-Push لأجهزته. يمنع التكرار بجدول أحداث بمفتاح فريد لكل حدث
/// (لا لكل يوم) فيُطلق مرّة واحدة. الروتنة: المدير المباشر (حين يذكرها الجمهور) + المشرفون
/// (أدمن/HR المرتبطون بموظف). منطق المطابقة الزمنية نقيّ وقابل للاختبار.
/// </summary>
public static class NotificationRuleGenerator
{
    public enum RuleKind { Birthday, Anniversary, ContractExpiry, ProbationEnding }

    /// <summary>ربط اسم القاعدة العربي (كما يُبذَر) بنوع المولّد. غير المذكور = غير مدعوم v1.</summary>
    public static RuleKind? MapRuleName(string name) => name?.Trim() switch
    {
        "عيد ميلاد موظف" => RuleKind.Birthday,
        "ذكرى عمل موظف" => RuleKind.Anniversary,
        "عقود الموظفين" => RuleKind.ContractExpiry,
        "فترة التجربة" => RuleKind.ProbationEnding,
        _ => null
    };

    // ─────────────────────────── منطق المطابقة النقيّ (قابل للاختبار) ───────────────────────────

    /// <summary>هل يوافق اليوم ذكرى سنوية للتاريخ (نفس الشهر/اليوم)؟ يعامل 29 فبراير كـ28 في السنة غير الكبيسة.</summary>
    public static bool IsAnnualMatchToday(DateOnly? date, DateOnly today)
    {
        if (date is not { } d) return false;
        if (d.Month == today.Month && d.Day == today.Day) return true;
        // 29 فبراير في سنة غير كبيسة ⟹ يُطلق في 28 فبراير
        return d is { Month: 2, Day: 29 } && today is { Month: 2, Day: 28 }
               && !DateTime.IsLeapYear(today.Year);
    }

    /// <summary>عدد سنوات الخدمة إن كان اليوم ذكرى التعيين (وإلا 0). صفر أو أقل ⟹ لا ذكرى (يوم التعيين نفسه).</summary>
    public static int AnniversaryYears(DateOnly? hireDate, DateOnly today)
    {
        if (!IsAnnualMatchToday(hireDate, today)) return 0;
        var years = today.Year - hireDate!.Value.Year;
        return years > 0 ? years : 0;
    }

    /// <summary>هل يقع التاريخ الهدف ضمن نافذة [اليوم، اليوم+daysBefore]؟ (لا يشمل الماضي).</summary>
    public static bool WithinWindow(DateOnly? target, DateOnly today, int daysBefore)
    {
        if (target is not { } t) return false;
        if (t < today) return false;
        return (t.DayNumber - today.DayNumber) <= Math.Max(0, daysBefore);
    }

    /// <summary>تاريخ انتهاء فترة التجربة = تاريخ الأساس + المدة (+ أيام التمديد إن كان مسموحاً).</summary>
    public static DateOnly? ProbationEnd(DateOnly? startDate, string unit, int value,
        bool allowExtension, int extensionDays)
    {
        if (startDate is not { } start || value <= 0) return null;
        var end = unit?.Trim().ToLowerInvariant() switch
        {
            "month" => start.AddMonths(value),
            "week" => start.AddDays(value * 7),
            _ => start.AddDays(value) // Day (الافتراضي)
        };
        if (allowExtension && extensionDays > 0) end = end.AddDays(extensionDays);
        return end;
    }

    // ─────────────────────────── التوليد والإطلاق ───────────────────────────

    public sealed record GenerationResult(int NewEvents, int NotificationsCreated, int PushDelivered);

    public static async Task EnsureEventLogAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('NexoraNotificationEvents', 'U') IS NULL
BEGIN
    CREATE TABLE NexoraNotificationEvents
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventKey nvarchar(200) NOT NULL,
        RuleKind nvarchar(40) NOT NULL,
        SubjectEmployeeId int NOT NULL,
        RecipientCount int NOT NULL DEFAULT(0),
        PushDelivered int NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_NexoraNotifEvents_Key ON NexoraNotificationEvents (EventKey);
END;
""");
    }

    /// <summary>
    /// يشغّل دورة توليد لتاريخ محدد (تاريخ بغداد). idempotent: كل حدث جديد فقط يُطلق ويُسجَّل.
    /// </summary>
    public static async Task<GenerationResult> GenerateAsync(
        ApplicationDbContext db, IWebPushSender webPush, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        await HrSettingsStore.EnsureTablesAsync(db);
        await EnsureEventLogAsync(db);

        // القواعد المفعّلة المدعومة v1
        var rules = (await HrSettingsStore.LoadNotificationRulesAsync(db))
            .Where(r => r.IsEnabled)
            .Select(r => (Kind: MapRuleName(r.Name), r.DaysBefore, r.Audience))
            .Where(r => r.Kind is not null)
            .GroupBy(r => r.Kind!.Value)
            .ToDictionary(g => g.Key, g => g.First()); // قاعدة واحدة لكل نوع

        if (rules.Count == 0)
            return new GenerationResult(0, 0, 0);

        // إعدادات فترة التجربة — السياسة الأمّ (تُقرأ مرّة)
        var probUnit = await HrSettingsStore.GetAsync(db, "Probation.DurationUnit", "Day");
        var probValue = int.TryParse(await HrSettingsStore.GetAsync(db, "Probation.DurationValue", "90"), out var pv) ? pv : 90;
        var probBasis = await HrSettingsStore.GetAsync(db, "Probation.StartBasis", "HireDate");
        var probAllowExt = bool.TryParse(await HrSettingsStore.GetAsync(db, "Probation.AllowExtension", "False"), out var ae) && ae;
        var probExtDays = int.TryParse(await HrSettingsStore.GetAsync(db, "Probation.ExtensionDays", "0"), out var ed) ? ed : 0;

        var parentProbation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProbationPeriodModel.KeyDuration] = probValue.ToString(),
            [ProbationPeriodModel.KeyUnit] = probUnit,
            [ProbationPeriodModel.KeyBasis] = probBasis,
            [ProbationPeriodModel.KeyExtensionDays] = probExtDays.ToString()
        };

        // نُسَخ السياسة المشروطة. **تُقرأ الحقائق مرّة واحدة لكل الموظفين ويُحسم
        // بالذاكرة** — استعلامٌ لكل موظف كان سيعني 1356 دورة ذهاب وإياب بكل تشغيل
        // للكرون اليومي. وبلا نُسَخ لا يُقرأ شيء أصلاً، فالمسار القائم بلا كلفة.
        var probationOverrides = (await HrPolicyOverrideStore.LoadAsync(db, HrPolicyOverrideStore.PolicyProbation))
            .Select(row => row.ToOverride())
            .ToList();

        var factsByEmployee = probationOverrides.Count == 0
            ? new Dictionary<int, Dictionary<string, HrConditions.Fact>>()
            : (await HrConditionFacts.LoadAsync(db))
                .ToDictionary(row => row.Id, row => HrConditionFacts.Build(row, today));

        // المشرفون: أدمن/HR فعّالون مرتبطون بموظف
        var supervisorIds = (await HrmsDatabase.QueryAsync(
            db,
            """
SELECT DISTINCT su.EmployeeId
FROM SystemUsers su
WHERE su.IsActive = 1 AND su.IsDeleted = 0
  AND su.EmployeeId IS NOT NULL
  AND su.Role IN (1, 2);
""",
            _ => { },
            reader => HrmsDatabase.GetNullableInt(reader, "EmployeeId")))
            .Where(x => x is int).Select(x => x!.Value).ToHashSet();

        // الموظفون الفعّالون بالحقول اللازمة
        var employees = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, FullName, BirthDate, HireDate, JoiningDate, ContractEndDate, DirectManagerId
FROM Employees
WHERE IsActive = 1 AND IsDeleted = 0;
""",
            _ => { },
            reader => new EmpRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "FullName"),
                HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                HrmsDatabase.GetDateOnly(reader, "HireDate"),
                HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                HrmsDatabase.GetNullableInt(reader, "DirectManagerId")));

        // الأحداث المُطلَقة سابقاً (منع التكرار)
        var firedKeys = (await HrmsDatabase.QueryAsync(
            db,
            "SELECT EventKey FROM NexoraNotificationEvents;",
            _ => { },
            reader => HrmsDatabase.GetString(reader, "EventKey")))
            .ToHashSet();

        // بناء أحداث اليوم المرشّحة
        var candidates = new List<PendingEvent>();
        foreach (var emp in employees)
        {
            // فترة التجربة قد تختلف لهذا الموظف بنسخة سياسة مشروطة (فرع/فئة/راتب).
            var probation = parentProbation;
            if (probationOverrides.Count > 0 && factsByEmployee.TryGetValue(emp.Id, out var empFacts))
            {
                probation = (Dictionary<string, string>)HrPolicyResolver
                    .Resolve(parentProbation, probationOverrides, empFacts).Values;
            }

            var empBasis = probation.GetValueOrDefault(ProbationPeriodModel.KeyBasis, probBasis);
            var empUnit = probation.GetValueOrDefault(ProbationPeriodModel.KeyUnit, probUnit);
            var empValue = int.TryParse(probation.GetValueOrDefault(ProbationPeriodModel.KeyDuration), out var rv) ? rv : probValue;
            var empExtDays = int.TryParse(probation.GetValueOrDefault(ProbationPeriodModel.KeyExtensionDays), out var rd) ? rd : probExtDays;

            foreach (var (kind, rule) in rules)
            {
                var ev = BuildEvent(kind, rule.DaysBefore, emp, today,
                    empBasis, empUnit, empValue, probAllowExt, empExtDays);
                if (ev is not null && !firedKeys.Contains(ev.EventKey))
                    candidates.Add(ev with { IncludeManager = rule.Audience.Contains("مدير") });
            }
        }

        if (candidates.Count == 0)
            return new GenerationResult(0, 0, 0);

        var utcNow = DateTime.UtcNow;
        int created = 0, pushed = 0;

        foreach (var ev in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // روتنة المستلمين: المشرفون (أدمن/HR مرتبطون بموظف) + المدير المباشر حين يذكره الجمهور.
            var recipients = new HashSet<int>(supervisorIds);
            if (ev.IncludeManager && ev.DirectManagerId is int mgr) recipients.Add(mgr);

            // احتياطي: لو لم يُحَلّ أي مشرف مرتبط بموظف (حسابات الأدمن/HR غير مربوطة بموظفين)
            // نُسلّم للمدير المباشر رغم أن الجمهور «مشرفون فقط» — فالإشعار يصل صندوقاً فعلياً
            // (بوابة الموظف) بدل أن يُسجَّل الحدث بلا مستلم. المدير المباشر دائماً موظف.
            if (recipients.Count == 0 && ev.DirectManagerId is int fallbackMgr)
                recipients.Add(fallbackMgr);

            recipients.Remove(ev.SubjectEmployeeId); // لا يُشعَر الموظف بنفسه لحدثه

            int rowPush = 0;
            if (recipients.Count > 0)
            {
                var notification = new UserNotification
                {
                    AnnouncementGroupId = null,
                    NotificationType = ev.Type,
                    TitleAr = ev.Title,
                    MessageAr = ev.Body,
                    Url = $"/Employees/Profile?id={ev.SubjectEmployeeId}",
                    CreatedAtUtc = utcNow,
                    CreatedAt = utcNow
                };
                foreach (var rid in recipients)
                {
                    notification.Recipients.Add(new UserNotificationRecipient
                    {
                        EmployeeId = rid,
                        IsRead = false,
                        CreatedAt = utcNow
                    });
                }
                db.Set<UserNotification>().Add(notification);
                await db.SaveChangesAsync(cancellationToken);
                created++;

                if (webPush.IsEnabled)
                {
                    var payload = new PushPayload(ev.Title, ev.Body, notification.Url);
                    foreach (var rid in recipients)
                        rowPush += await webPush.SendToEmployeeAsync(db, rid, payload, cancellationToken);
                }
                pushed += rowPush;
            }

            // القناة الثانية (back-office): صفّ SystemNotifications موجَّه للـHR — يراه أدوار HR
            // (رمز «HR») والأدمن (يرى كل ما لا يستهدف الموظفين). هذا يوصِل الحدث للأدمن/HR
            // غير المربوطين بموظف — الفجوة التي كانت تجعل الجدول يُكتَب ولا يُقرأ. يُكتب مرّة
            // واحدة لكل حدث جديد (نفس idempotency جدول الأحداث).
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (@Title, @Message, N'HR', @Url);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Title", ev.Title);
                    HrmsDatabase.AddParameter(command, "@Message", ev.Body);
                    HrmsDatabase.AddParameter(command, "@Url", $"/Employees/Profile?id={ev.SubjectEmployeeId}");
                });

            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO NexoraNotificationEvents (EventKey, RuleKind, SubjectEmployeeId, RecipientCount, PushDelivered)
VALUES (@Key, @Kind, @Emp, @Recipients, @Push);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Key", ev.EventKey);
                    HrmsDatabase.AddParameter(command, "@Kind", ev.Kind.ToString());
                    HrmsDatabase.AddParameter(command, "@Emp", ev.SubjectEmployeeId);
                    HrmsDatabase.AddParameter(command, "@Recipients", recipients.Count);
                    HrmsDatabase.AddParameter(command, "@Push", rowPush);
                });
        }

        return new GenerationResult(candidates.Count, created, pushed);
    }

    /// <summary>يبني حدثاً مرشّحاً لموظف/نوع إن انطبقت المطابقة الزمنية اليوم (وإلا null). نقيّ.</summary>
    private static PendingEvent? BuildEvent(RuleKind kind, int daysBefore, EmpRow emp, DateOnly today,
        string probBasis, string probUnit, int probValue, bool probAllowExt, int probExtDays)
    {
        switch (kind)
        {
            case RuleKind.Birthday when IsAnnualMatchToday(emp.BirthDate, today):
                return New(kind, UserNotificationType.Birthday, emp,
                    $"birthday:{emp.Id}:{today.Year}",
                    "🎂 عيد ميلاد موظف",
                    $"اليوم عيد ميلاد الموظف {emp.FullName}. لا تنسَ تهنئته.");

            case RuleKind.Anniversary:
            {
                var years = AnniversaryYears(emp.HireDate, today);
                if (years <= 0) return null;
                return New(kind, UserNotificationType.WorkAnniversary, emp,
                    $"anniv:{emp.Id}:{today.Year}",
                    "🎉 ذكرى عمل موظف",
                    $"اليوم يُتمّ الموظف {emp.FullName} عامه {years} في العمل.");
            }

            case RuleKind.ContractExpiry when WithinWindow(emp.ContractEndDate, today, daysBefore):
                return New(kind, UserNotificationType.ContractExpiry, emp,
                    $"contract:{emp.Id}:{emp.ContractEndDate:yyyy-MM-dd}",
                    "📄 قرب انتهاء عقد موظف",
                    $"عقد الموظف {emp.FullName} ينتهي بتاريخ {emp.ContractEndDate:yyyy-MM-dd}. يُرجى المتابعة.");

            case RuleKind.ProbationEnding:
            {
                var start = probBasis.Equals("JoiningDate", StringComparison.OrdinalIgnoreCase)
                    ? (emp.JoiningDate ?? emp.HireDate)
                    : emp.HireDate;
                var end = ProbationEnd(start, probUnit, probValue, probAllowExt, probExtDays);
                if (!WithinWindow(end, today, daysBefore)) return null;
                return New(kind, UserNotificationType.ProbationEnding, emp,
                    $"probation:{emp.Id}:{end:yyyy-MM-dd}",
                    "🧪 قرب انتهاء فترة تجربة موظف",
                    $"فترة تجربة الموظف {emp.FullName} تنتهي بتاريخ {end:yyyy-MM-dd}. يُرجى اتخاذ قرار التثبيت.");
            }
        }
        return null;
    }

    private static PendingEvent New(RuleKind kind, UserNotificationType type, EmpRow emp,
        string key, string title, string body) =>
        new(key, kind, type, emp.Id, emp.DirectManagerId, title, body, false);

    private sealed record EmpRow(int Id, string FullName, DateOnly? BirthDate, DateOnly? HireDate,
        DateOnly? JoiningDate, DateOnly? ContractEndDate, int? DirectManagerId);

    private sealed record PendingEvent(string EventKey, RuleKind Kind, UserNotificationType Type,
        int SubjectEmployeeId, int? DirectManagerId, string Title, string Body, bool IncludeManager);
}
