using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// مولّد مركز الإشعارات: يقرأ قواعد <c>ZynoraNotificationRules</c> المفعّلة،
/// يطابقها بموظفي الشركة (عيد ميلاد · ذكرى عمل · قرب انتهاء العقد · قرب انتهاء فترة
/// التجربة)، ويُطلق إشعاراً فعلياً لكل حدث جديد: صندوق داخل النظام (<see cref="UserNotification"/>)
/// لكل مستلم + دفع Web-Push لأجهزته. يمنع التكرار بجدول أحداث بمفتاح فريد لكل حدث
/// (لا لكل يوم) فيُطلق مرّة واحدة. التوجيه حسب الجمهور واسم مستخدم المشرف ضمن شركة
/// الموظف، أو الموظف نفسه حصراً. منطق المطابقة الزمنية والتوجيه نقيّ وقابل للاختبار.
/// </summary>
public static class NotificationRuleGenerator
{
    public enum RuleKind { Birthday, Anniversary, ContractExpiry, ProbationEnding, Feedback, FeedbackReply, ContractRenewal, ContractExtension,
        Welcome, Rehire, Violation, DisciplinaryAction, Satisfaction, Retirement, DocumentExpiry, ExitInterview, Poll, Farewell }

    /// <summary>ربط اسم القاعدة العربي بنوع مصدر فعلي. غير المذكور غير مرتبط بعد.</summary>
    public static RuleKind? MapRuleName(string name) => name?.Trim() switch
    {
        "عيد ميلاد موظف" => RuleKind.Birthday,
        "ذكرى عمل موظف" => RuleKind.Anniversary,
        "عقود الموظفين" => RuleKind.ContractExpiry,
        "فترة التجربة" => RuleKind.ProbationEnding,
        "اقتراحات وشكاوي" => RuleKind.Feedback,
        "الرد على الاقتراحات والشكاوي" => RuleKind.FeedbackReply,
        "تجديد عقد" => RuleKind.ContractRenewal,
        "تمديد العقد" => RuleKind.ContractExtension,
        "الترحيب بموظف جديد" => RuleKind.Welcome,
        "إعادة تعيين الموظف" => RuleKind.Rehire,
        "مخالفات الموظفين" => RuleKind.Violation,
        "الإجراءات التأديبية المتخذة" => RuleKind.DisciplinaryAction,
        "رضا الموظفين" => RuleKind.Satisfaction,
        "سن التقاعد" => RuleKind.Retirement,
        "انتهاء صلاحية الوثيقة" => RuleKind.DocumentExpiry,
        "مقابلات نهاية الخدمة" => RuleKind.ExitInterview,
        "الإنتخابات و إستطلاعات الرأي" => RuleKind.Poll,
        "وداع موظف" => RuleKind.Farewell,
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

    public sealed record GenerationResult(int NewEvents, int NotificationsCreated, int PushDelivered, bool LockAcquired = true);

    /// <summary>
    /// يشغّل دورة توليد لتاريخ محدد (تاريخ بغداد). idempotent: كل حدث جديد فقط يُطلق ويُسجَّل.
    /// </summary>
    public static async Task<GenerationResult> GenerateAsync(
        ApplicationDbContext db, IWebPushSender webPush, DateOnly today,
        CancellationToken cancellationToken = default, CompanyScope? companyScope = null, bool includeCalendarEvents = true)
    {
        GenerationResult result = new(0, 0, 0, false);
        await SqlDistributedLock.TryRunAsync(db, "ZYNORA.NotificationRuleGenerator", async () =>
            result = await GenerateLockedAsync(db, webPush, today, cancellationToken, companyScope, includeCalendarEvents), cancellationToken);
        return result;
    }

    private static async Task<GenerationResult> GenerateLockedAsync(
        ApplicationDbContext db, IWebPushSender webPush, DateOnly today,
        CancellationToken cancellationToken, CompanyScope? companyScope, bool includeCalendarEvents)
    {
        await HrSettingsStore.EnsureTablesAsync(db);

        // القواعد المفعّلة المدعومة v1
        var rules = (await HrSettingsStore.LoadNotificationRulesAsync(db))
            .Select(r => (Kind: MapRuleName(NotificationRuleSettings.ReferenceName(r.Name)), Row: r, r.Id, Name: NotificationRuleSettings.ReferenceName(r.Name)))
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
            [HrPolicyOverrideStore.ProbationKeyDuration] = probValue.ToString(),
            [HrPolicyOverrideStore.ProbationKeyUnit] = probUnit,
            [HrPolicyOverrideStore.ProbationKeyBasis] = probBasis,
            [HrPolicyOverrideStore.ProbationKeyExtensionDays] = probExtDays.ToString()
        };

        // نُسَخ السياسة المشروطة. **تُقرأ الحقائق مرّة واحدة لكل الموظفين ويُحسم
        // بالذاكرة** — استعلامٌ لكل موظف كان سيعني 1356 دورة ذهاب وإياب بكل تشغيل
        // للكرون اليومي. وبلا نُسَخ لا يُقرأ شيء أصلاً، فالمسار القائم بلا كلفة.
        var probationOverrides = (includeCalendarEvents ? await HrPolicyOverrideStore.LoadAsync(db, HrPolicyOverrideStore.PolicyProbation) : [])
            .Select(row => row.ToOverride())
            .ToList();

        // المشرفون: أدمن/HR فعّالون مرتبطون بموظف
        // Background generation is system-authorized; manual generation receives the caller's scope.
        var companyQuery = db.Companies.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted);
        if (companyScope is not null && !companyScope.IsUnrestricted)
            companyQuery = companyQuery.Where(c => companyScope.AllowedCompanyIds.Contains(c.Id));
        var companies = await companyQuery.Select(c => new { c.Id, c.TenantId }).ToListAsync(cancellationToken);
        var settingsByCompany = new Dictionary<int, Dictionary<int, NotificationRuleSettings>>();
        foreach (var company in companies)
            settingsByCompany[company.Id] = await NotificationRuleSettings.LoadCompanyAsync(db, company.Id);
        NotificationRuleSettings Settings(int companyId, RuleKind kind) =>
            settingsByCompany[companyId].GetValueOrDefault(rules[kind].Id) ?? NotificationRuleSettings.FromLegacy(rules[kind].Row);
        bool Enabled(NotificationRuleSettings settings, RuleKind kind) => settings.IsEnabled && NotificationRuleSettings.Valid(settings)
            && (kind != RuleKind.Farewell || settings.InApp) // Personal-email farewell needs a separate consented channel.
            && NotificationRuleCatalog.AllowsSettings(rules[kind].Name, settings)
            && (NotificationEventSources.Describe(kind) is null || settings.DaysBefore == 0);
        var factsByEmployee = probationOverrides.Count == 0
            ? new Dictionary<int, Dictionary<string, HrConditions.Fact>>()
            : (await HrConditionFacts.LoadAsync(db, authorizationScope: CompanyScope.ForCompanies(companies.Select(c => c.Id))))
                .ToDictionary(row => row.Id, row => HrConditionFacts.Build(row, today));
        var usersByCompany = new Dictionary<int, List<CompanyUser>>();
        var employees = new List<EmpRow>();
        foreach (var company in companies)
        {
        usersByCompany[company.Id] = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT DISTINCT su.EmployeeId, su.Role, su.UserName,
    CASE WHEN EXISTS (SELECT 1 FROM SystemUsers otherUser WHERE otherUser.UserName = su.UserName
        AND otherUser.TenantId <> su.TenantId AND otherUser.IsActive = 1 AND otherUser.IsDeleted = 0)
        THEN 0 ELSE 1 END AS BackOfficeAllowed
FROM SystemUsers su
LEFT JOIN Employees e ON e.Id = su.EmployeeId AND e.IsActive = 1 AND e.IsDeleted = 0
WHERE su.IsActive = 1 AND su.IsDeleted = 0
  AND su.TenantId = @TenantId
  AND (e.CompanyId = @CompanyId OR su.EmployeeId IS NULL);
""",
            command => { HrmsDatabase.AddParameter(command, "@TenantId", company.TenantId); HrmsDatabase.AddParameter(command, "@CompanyId", company.Id); },
            reader => new CompanyUser(HrmsDatabase.GetNullableInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "UserName"), HrmsDatabase.GetInt(reader, "Role"), HrmsDatabase.GetInt(reader, "BackOfficeAllowed") == 1));

        // الموظفون الفعّالون بالحقول اللازمة
        employees.AddRange(await HrmsDatabase.QueryAsync(
            db,
            """
SELECT e.Id, e.FullName, e.BirthDate, e.HireDate, e.JoiningDate, e.ContractEndDate,
       manager.Id AS DirectManagerId, e.DepartmentId, e.CompanyId,
       (SELECT TOP (1) RetirementAge FROM EmployeeFinancialInfos fi WHERE fi.EmployeeId = e.Id ORDER BY fi.Id DESC) AS RetirementAge
FROM Employees e
LEFT JOIN Employees manager ON manager.Id = e.DirectManagerId AND manager.CompanyId = e.CompanyId
    AND manager.IsActive = 1 AND manager.IsDeleted = 0
WHERE e.IsActive = 1 AND e.IsDeleted = 0 AND e.CompanyId = @CompanyId;
""",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", company.Id),
            reader => new EmpRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "FullName"),
                HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                HrmsDatabase.GetDateOnly(reader, "HireDate"),
                HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                HrmsDatabase.GetNullableInt(reader, "DirectManagerId"),
                HrmsDatabase.GetInt(reader, "DepartmentId"),
                HrmsDatabase.GetInt(reader, "CompanyId"), HrmsDatabase.GetNullableInt(reader, "RetirementAge"))));
        }

        // الأحداث المُطلَقة سابقاً (منع التكرار)
        var firedKeys = (await HrmsDatabase.QueryAsync(
            db,
            "SELECT EventKey FROM ZynoraNotificationEvents WHERE RuleKind IN (N'Birthday', N'Anniversary', N'ContractExpiry', N'ProbationEnding', N'Retirement', N'DocumentExpiry');",
            _ => { },
            reader => HrmsDatabase.GetString(reader, "EventKey")))
            .ToHashSet();

        // بناء أحداث اليوم المرشّحة
        var candidates = new List<PendingEvent>();
        // رئيس وحدة مؤقت: خلال نيابةٍ سارية يُوجَّه ما هو «للمدير» للرئيس المؤقت،
        // وإلا بقيت إشعارات المدير المسافر بلا قارئ — وهو سبب وجود الميزة أصلاً.
        var actingHeads = await TemporaryHeadStore.LoadAllocationsAsync(db);
        var eligibleManagers = employees.Select(e => (e.CompanyId, e.Id)).ToHashSet();

        foreach (var employee in employees)
        {
            var emp = actingHeads.Count == 0
                ? employee
                : employee with
                {
                    DirectManagerId = TemporaryHeadPolicy.EffectiveManagerId(
                        employee.Id, employee.DepartmentId, employee.DirectManagerId, actingHeads, today)
                };
            // Temporary allocations are legacy global rows; never route outside the subject company.
            if (emp.DirectManagerId is int actingManager && !eligibleManagers.Contains((emp.CompanyId, actingManager)))
                emp = emp with { DirectManagerId = null };

            // فترة التجربة قد تختلف لهذا الموظف بنسخة سياسة مشروطة (فرع/فئة/راتب).
            var probation = parentProbation;
            if (probationOverrides.Count > 0 && factsByEmployee.TryGetValue(emp.Id, out var empFacts))
            {
                probation = (Dictionary<string, string>)HrPolicyResolver
                    .Resolve(parentProbation, probationOverrides, empFacts).Values;
            }

            var empBasis = probation.GetValueOrDefault(HrPolicyOverrideStore.ProbationKeyBasis, probBasis);
            var empUnit = probation.GetValueOrDefault(HrPolicyOverrideStore.ProbationKeyUnit, probUnit);
            var empValue = int.TryParse(probation.GetValueOrDefault(HrPolicyOverrideStore.ProbationKeyDuration), out var rv) ? rv : probValue;
            var empExtDays = int.TryParse(probation.GetValueOrDefault(HrPolicyOverrideStore.ProbationKeyExtensionDays), out var rd) ? rd : probExtDays;

            foreach (var (kind, rule) in rules.Where(r => includeCalendarEvents && NotificationEventSources.Describe(r.Key) is null))
            {
                var settings = Settings(emp.CompanyId, kind);
                if (!Enabled(settings, kind)) continue;
                var ev = BuildEvent(kind, settings.DaysBefore, emp, today,
                    empBasis, empUnit, empValue, probAllowExt, empExtDays);
                if (ev is not null && !firedKeys.Contains(ev.EventKey))
                    candidates.Add(ev with { Audience = settings.Audience, SupervisorName = settings.SupervisorName });
            }
        }

        // Harvest committed source events within each authorized company. No historical replay.
        var employeesById = employees.ToDictionary(e => e.Id);
        if (includeCalendarEvents && rules.ContainsKey(RuleKind.DocumentExpiry))
        foreach (var company in companies)
        {
            var settings = Settings(company.Id, RuleKind.DocumentExpiry);
            if (!Enabled(settings, RuleKind.DocumentExpiry)) continue;
            var documents = await HrmsDatabase.QueryAsync(db, """
SELECT d.Id, d.EmployeeId, d.ExpiryDate FROM EmployeeDocuments d
JOIN Employees e ON e.Id = d.EmployeeId AND e.CompanyId = @CompanyId AND e.IsActive = 1 AND e.IsDeleted = 0
WHERE d.ExpiryDate >= @Today AND d.ExpiryDate <= @End;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", company.Id);
                HrmsDatabase.AddParameter(command, "@Today", today.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@End", today.AddDays(settings.DaysBefore).ToDateTime(TimeOnly.MinValue));
            }, reader => (Id: HrmsDatabase.GetInt(reader, "Id"), EmployeeId: HrmsDatabase.GetInt(reader, "EmployeeId"), Expiry: HrmsDatabase.GetDateOnly(reader, "ExpiryDate")));
            foreach (var document in documents)
            {
                if (!employeesById.TryGetValue(document.EmployeeId, out var employee)) continue;
                var key = $"document:{company.Id}:{document.Id}:{document.Expiry:yyyy-MM-dd}";
                if (firedKeys.Contains(key)) continue;
                var manager = TemporaryHeadPolicy.EffectiveManagerId(employee.Id, employee.DepartmentId, employee.DirectManagerId, actingHeads, today);
                if (manager is int managerId && !eligibleManagers.Contains((company.Id, managerId))) manager = null;
                candidates.Add(new PendingEvent(key, RuleKind.DocumentExpiry, UserNotificationType.RequestWorkflow, employee.Id, manager,
                    "انتهاء صلاحية وثيقة موظف", $"تنتهي صلاحية وثيقة للموظف {employee.FullName} بتاريخ {document.Expiry:yyyy-MM-dd}.",
                    company.Id, settings.Audience, settings.SupervisorName));
            }
        }
        foreach (var (kind, rule) in rules)
        {
            var source = NotificationEventSources.Describe(kind);
            if (source is null) continue;
            if (kind == RuleKind.Farewell && !await EndServiceAccessStore.IsReadyAsync(db)) continue;
            foreach (var company in companies)
            {
                var settings = Settings(company.Id, kind);
                if (!Enabled(settings, kind)) continue;
                var since = settings.EnabledSinceUtc == default
                    ? await NotificationEventSources.EnabledSinceAsync(db, rule.Id) : settings.EnabledSinceUtc;
                var configuredSource = NotificationEventSources.Describe(kind, settings.WelcomeBasis)!;
                var rows = await HrmsDatabase.QueryAsync(db, NotificationEventSources.ScopedQuery(configuredSource), command =>
                {
                    HrmsDatabase.AddParameter(command, "@CompanyId", company.Id);
                    HrmsDatabase.AddParameter(command, "@Kind", kind.ToString());
                    HrmsDatabase.AddParameter(command, "@EnabledSince", since);
                    HrmsDatabase.AddParameter(command, "@Now", DateTime.UtcNow);
                }, reader => new NotificationEventSources.Event(HrmsDatabase.GetInt(reader, "Id"),
                    HrmsDatabase.GetInt(reader, "EmployeeId"), Convert.ToDateTime(reader["OccurredAt"])));
                if (configuredSource.AllowsDepartedSubject)
                {
                    // Exit-survey subjects may have left; recipients still come exclusively
                    // from the active company set. Do not add departed subjects to that set.
                    var missingIds = rows.Select(r => r.EmployeeId).Where(id => !employeesById.ContainsKey(id)).Distinct().ToArray();
                    if (missingIds.Length > 0)
                    {
                        var departed = await db.Employees.AsNoTracking().Where(e => e.CompanyId == company.Id && !e.IsDeleted && missingIds.Contains(e.Id))
                            .Select(e => new EmpRow(e.Id, e.FullName, e.BirthDate, e.HireDate, e.JoiningDate, e.ContractEndDate,
                                e.DirectManagerId, e.DepartmentId, company.Id, null)).ToListAsync(cancellationToken);
                        foreach (var subject in departed) employeesById[subject.Id] = subject;
                    }
                }
                if (configuredSource.CollectiveEvent)
                {
                    // One publication, one acknowledgement and one supervisor delivery, not one per voter.
                    foreach (var publication in rows.GroupBy(r => (r.Id, r.OccurredAt)))
                    {
                        var voters = publication.Select(r => r.EmployeeId).Where(id => eligibleManagers.Contains((company.Id, id))).Distinct().Order().ToArray();
                        if (voters.Length == 0) continue;
                        var row = publication.First();
                        candidates.Add(new PendingEvent(NotificationEventSources.Key(kind, company.Id, row), kind,
                            UserNotificationType.RequestWorkflow, voters[0], null, rule.Name, "تصويت جديد متاح للمشاركة.", company.Id,
                            settings.Audience, settings.SupervisorName, voters));
                    }
                    continue;
                }
                foreach (var row in rows)
                {
                    var key = NotificationEventSources.Key(kind, company.Id, row);
                    if (firedKeys.Contains(key) || !employeesById.TryGetValue(row.EmployeeId, out var employee)) continue;
                    var manager = TemporaryHeadPolicy.EffectiveManagerId(employee.Id, employee.DepartmentId,
                        employee.DirectManagerId, actingHeads, today);
                    if (manager is int id && !eligibleManagers.Contains((company.Id, id))) manager = null;
                    candidates.Add(new PendingEvent(key, kind, UserNotificationType.RequestWorkflow,
                        employee.Id, manager, rule.Name, $"{rule.Name}: {employee.FullName}.", company.Id,
                        settings.Audience, settings.SupervisorName));
                }
            }
        }

        if (candidates.Count == 0)
            return new GenerationResult(0, 0, 0);

        var utcNow = DateTime.UtcNow;
        int created = 0, pushed = 0, events = 0;

        foreach (var ev in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // روتنة المستلمين: المشرفون (أدمن/HR مرتبطون بموظف) + المدير المباشر حين يذكره الجمهور.
            var companyUsers = usersByCompany[ev.CompanyId];
            var settings = Settings(ev.CompanyId, ev.Kind);
            var allowedGroup = ev.Kind == RuleKind.Poll ? ev.VoterIds ?? [] : ev.Audience == NotificationRoutingPolicy.AllEmployees
                ? employees.Where(e => e.CompanyId == ev.CompanyId).Select(e => e.Id).ToArray()
                : settings.GroupMembers.Where(id => eligibleManagers.Contains((ev.CompanyId, id))).ToArray();
            var routing = NotificationRoutingPolicy.Resolve(ev.Audience, ev.SubjectEmployeeId, ev.DirectManagerId,
                companyUsers.Where(u => u.Role is 1 or 2).Select(u => new NotificationRoutingPolicy.Supervisor(u.EmployeeId, u.Username, u.BackOfficeAllowed)),
                companyUsers.Select(u => new NotificationRoutingPolicy.Supervisor(u.EmployeeId, u.Username, u.BackOfficeAllowed)), ev.SupervisorName, allowedGroup);
            var recipients = routing.EmployeeIds;
            if (ev.Kind == RuleKind.Farewell)
            {
                // A departed subject is eligible only inside this explicit grace window;
                // it never joins the active-company set used for any other notification.
                var ownAccess = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(*) FROM EndServiceAccessSchedules s
JOIN Employees e ON e.Id = s.EmployeeId AND e.CompanyId = s.CompanyId
WHERE s.EmployeeId = @EmployeeId AND s.CompanyId = @CompanyId AND e.IsActive = 0 AND e.IsDeleted = 0
  AND s.Immediate = 0 AND s.NotificationEligibleAtUtc <= SYSUTCDATETIME() AND s.AccessEndsAtUtc > SYSUTCDATETIME()
  AND s.EndServiceId = (SELECT MAX(es.Id) FROM EmployeeEndServices es WHERE es.EmployeeId = e.Id);
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", ev.SubjectEmployeeId);
                    HrmsDatabase.AddParameter(command, "@CompanyId", ev.CompanyId);
                });
                if (ownAccess == 0) continue; // no out-of-window acknowledgement or delayed delivery
            }
            if (recipients.Count == 0 && routing.BackOfficeUsers.Count == 0) continue; // no false acknowledgement
            if (!settings.InApp && recipients.Count == 0) continue; // unlinked back-office accounts have no employee email target
            var url = ev.Audience is NotificationRoutingPolicy.Employee or NotificationRoutingPolicy.Self or NotificationRoutingPolicy.AllEmployees
                or NotificationRoutingPolicy.Specific or NotificationRoutingPolicy.Group or NotificationRoutingPolicy.Voters or NotificationRoutingPolicy.VotersAndSupervisors ? "/EmployeePortal"
                : NotificationEventSources.Describe(ev.Kind)?.BackOfficeUrl ?? $"/Employees/Profile?id={ev.SubjectEmployeeId}";
            var employeeName = ev.Kind == RuleKind.Poll ? "" : employeesById[ev.SubjectEmployeeId].FullName;
            if (ev.Kind == RuleKind.Farewell)
            {
                var serviceId = ev.EventKey.Split(':')[3]; // generated from the closed source catalog, not user input
                url = EndServiceAccessStore.FarewellPath + "?serviceId=" + serviceId;
            }
            var ruleName = NotificationRuleSettings.ReferenceName(rules[ev.Kind].Name);
            var title = NotificationTemplate.Render(settings.TitleTemplate, ev.Title, employeeName, ruleName, today, ev.Body);
            var body = NotificationTemplate.Render(settings.BodyTemplate, ev.Body, employeeName, ruleName, today, ev.Body);
            // Expanded values are data, not email headers; template validation alone
            // does not prevent a newline inside an employee's stored display name.
            title = title.Replace('\r', ' ').Replace('\n', ' ');
            title = title[..Math.Min(title.Length, 200)];
            body = body[..Math.Min(body.Length, 4000)];
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            int rowPush = 0;
            if (settings.InApp && recipients.Count > 0)
            {
                var notification = new UserNotification
                {
                    AnnouncementGroupId = null,
                    NotificationType = ev.Type,
                    TitleAr = title,
                    MessageAr = body,
                    Url = url,
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

            }

            // Per-user back-office rows, not a global HR broadcast. Employee target token excludes
            // the admin's broad role filter, while TargetUser still matches the intended HR/admin.
            // Ambiguous usernames across tenants are excluded because this legacy table has no TenantId.
            foreach (var username in settings.InApp ? routing.BackOfficeUsers : [])
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO SystemNotifications (Title, Message, TargetRole, TargetUser, Url)
VALUES (@Title, @Message, N'Employee', @User, @Url);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Title", title);
                    HrmsDatabase.AddParameter(command, "@Message", body);
                    HrmsDatabase.AddParameter(command, "@Url", ev.Kind == RuleKind.Poll ? "/Engagement"
                        : ev.Kind == RuleKind.Farewell ? "/Employees/Lifecycle" : url);
                    HrmsDatabase.AddParameter(command, "@User", username);
                });

            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO ZynoraNotificationEvents (EventKey, RuleKind, SubjectEmployeeId, RecipientCount, PushDelivered)
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
            if (settings.Email)
                foreach (var recipient in recipients)
                    await NotificationRuleMailOutbox.EnqueueAsync(db, ev.EventKey, ev.CompanyId, recipient, title, body);
            await transaction.CommitAsync(cancellationToken);
            events++;
            // External push occurs only after durable inbox + event commit. Never roll back an inbox after sending.
            if (settings.InApp && webPush.IsEnabled)
            {
                var payload = new PushPayload(title, body, url);
                foreach (var rid in recipients)
                    rowPush += await webPush.SendToEmployeeAsync(db, rid, payload, cancellationToken);
            }
            pushed += rowPush;
            if (rowPush > 0)
                await HrmsDatabase.ExecuteAsync(db, "UPDATE ZynoraNotificationEvents SET PushDelivered = @Push WHERE EventKey = @Key;",
                    command => { HrmsDatabase.AddParameter(command, "@Push", rowPush); HrmsDatabase.AddParameter(command, "@Key", ev.EventKey); });
            firedKeys.Add(ev.EventKey);
        }

        return new GenerationResult(events, created, pushed);
    }

    /// <summary>يبني حدثاً مرشّحاً لموظف/نوع إن انطبقت المطابقة الزمنية اليوم (وإلا null). نقيّ.</summary>
    private static PendingEvent? BuildEvent(RuleKind kind, int daysBefore, EmpRow emp, DateOnly today,
        string probBasis, string probUnit, int probValue, bool probAllowExt, int probExtDays)
    {
        switch (kind)
        {
            case RuleKind.Birthday when WithinWindow(NotificationRoutingPolicy.NextAnnualDate(emp.BirthDate, today), today, daysBefore):
                return New(kind, UserNotificationType.Birthday, emp,
                    $"birthday:{emp.Id}:{NotificationRoutingPolicy.NextAnnualDate(emp.BirthDate, today)!.Value.Year}",
                    "🎂 عيد ميلاد موظف",
                    $"عيد ميلاد الموظف {emp.FullName} بتاريخ {NotificationRoutingPolicy.NextAnnualDate(emp.BirthDate, today):yyyy-MM-dd}. لا تنسَ تهنئته.");

            case RuleKind.Anniversary:
            {
                var target = NotificationRoutingPolicy.NextAnnualDate(emp.HireDate, today);
                if (!WithinWindow(target, today, daysBefore)) return null;
                var years = target!.Value.Year - emp.HireDate!.Value.Year;
                if (years <= 0) return null;
                return New(kind, UserNotificationType.WorkAnniversary, emp,
                    $"anniv:{emp.Id}:{target.Value.Year}",
                    "🎉 ذكرى عمل موظف",
                    $"يُتمّ الموظف {emp.FullName} عامه {years} في العمل بتاريخ {target:yyyy-MM-dd}.");
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
            case RuleKind.Retirement:
            {
                var target = RetirementDate(emp.BirthDate, emp.RetirementAge);
                if (!WithinWindow(target, today, daysBefore)) return null;
                return New(kind, UserNotificationType.RequestWorkflow, emp, $"retirement:{emp.Id}:{target:yyyy-MM-dd}",
                    "بلوغ سن التقاعد", $"يبلغ الموظف {emp.FullName} سن التقاعد المحدد بملفه بتاريخ {target:yyyy-MM-dd}.");
            }
        }
        return null;
    }

    private static PendingEvent New(RuleKind kind, UserNotificationType type, EmpRow emp,
        string key, string title, string body) =>
        new(key, kind, type, emp.Id, emp.DirectManagerId, title, body, emp.CompanyId, "", "");

    private sealed record EmpRow(int Id, string FullName, DateOnly? BirthDate, DateOnly? HireDate,
        DateOnly? JoiningDate, DateOnly? ContractEndDate, int? DirectManagerId, int DepartmentId, int CompanyId, int? RetirementAge);

    public static DateOnly? RetirementDate(DateOnly? birthDate, int? age) =>
        birthDate is { } birth && age is >= 16 and <= 100 && birth.Year + age.Value <= 9999 ? birth.AddYears(age.Value) : null;

    private sealed record CompanyUser(int? EmployeeId, string Username, int Role, bool BackOfficeAllowed);

    private sealed record PendingEvent(string EventKey, RuleKind Kind, UserNotificationType Type,
        int SubjectEmployeeId, int? DirectManagerId, string Title, string Body, int CompanyId, string Audience, string SupervisorName, int[]? VoterIds = null);
}
