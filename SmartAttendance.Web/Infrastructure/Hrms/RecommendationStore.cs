using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Attendance;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الإجراءات المقترحة (نمط كيان — قسمي 10 و13 بدراسة الحضور): مخرجات محرك القواعد.
/// «تحليل واسترجاع الاقتراحات» يقيّم القواعد النشطة على يوميات الشهر فيولّد اقتراحاً
/// لكل (موظف×يوم×قاعدة) — فريد، فلا تتكرر الاقتراحات بإعادة التحليل ولا يُعاد
/// توليد المتجاهل. القواعد التلقائية تُعتمد فوراً. اعتماد اقتراح «مخالفة» ينشئ
/// EmployeeViolationCase مرتبطة بمصدر «محرك الحضور».
/// </summary>
public static class RecommendationStore
{
    /// <summary>
    /// معرّفا قاعدتَي تعارض المغادرات الحارسان — سالبان عمداً فلا يصطدمان بمعرّفات
    /// قواعد AV الموجبة (IDENTITY) في مفتاح منع التكرار (EmployeeId, WorkDate, RuleId).
    /// </summary>
    public const int ConflictEarlyLeaveRuleId = -1;
    public const int ConflictLateReturnRuleId = -2;

    public sealed class Recommendation
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ActionType { get; set; } = "Violation";
        public string ActionText { get; set; } = string.Empty;
        public decimal ActionValue { get; set; }
        public string Status { get; set; } = "Pending";   // Pending | Approved | Ignored | Auto | Conflicted
        public int? ViolationCaseId { get; set; }
        public int? TransactionId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static string StatusLabel(string status) => status switch
    {
        "Pending" => "معلق",
        "Approved" => "معتمد",
        "Ignored" => "متجاهل",
        "Auto" => "تلقائي",
        "Conflicted" => "متعارض مع حركة",
        _ => status
    };

    /// <summary>
    /// تعارض الاقتراح مع حركة قائمة (نمط كيان «حركات متضاربة»): أثر عقابي (مخالفة/
    /// اقتطاع) على يوم عليه حركة معتمدة (إجازة/عطلة رسمية) لا يُنفَّذ تلقائياً بل
    /// يُعلَّم «متعارض» لمراجعة يدوية. أما الأثر المقصود لهذه الأيام (أوفرتايم/دخل/…)
    /// فليس تعارضاً.
    /// </summary>
    public static bool IsConflict(ShiftRuleStore.ShiftRule rule, DayAttendanceStore.DayRow day) =>
        ShiftRuleStore.EffectiveContext(day) is "Leave" or "Holiday"
        && rule.ActionType is "Violation" or "Deduction";

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AttendanceRecommendations', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceRecommendations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        RuleId int NOT NULL,
        RuleName nvarchar(200) NOT NULL DEFAULT(N''),
        Summary nvarchar(500) NOT NULL DEFAULT(N''),
        ActionType nvarchar(20) NOT NULL DEFAULT(N'Violation'),
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        Status nvarchar(20) NOT NULL DEFAULT(N'Pending'),
        ViolationCaseId int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        DecidedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_AttendanceRecommendations_Day_Rule
        ON AttendanceRecommendations (EmployeeId, WorkDate, RuleId);
END;

-- ترقية تدريجية: قيمة الأثر المالي + حركة منفَّذة مرتبطة
IF COL_LENGTH('AttendanceRecommendations', 'ActionValue') IS NULL
    ALTER TABLE AttendanceRecommendations ADD ActionValue decimal(12,2) NOT NULL DEFAULT(0);
IF COL_LENGTH('AttendanceRecommendations', 'TransactionId') IS NULL
    ALTER TABLE AttendanceRecommendations ADD TransactionId int NULL;
""");
    }

    /// <summary>
    /// «تحليل واسترجاع الاقتراحات»: تقييم القواعد النشطة على يوميات الشهر.
    /// </summary>
    /// <returns>(مقترحة جديدة، منها تلقائية معتمدة)</returns>
    public static async Task<(int Created, int Auto)> AnalyzeMonthAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int year, int month)
    {
        await EnsureAsync(dbContext);

        // قواعد AV + قواعد تعارض المغادرات (تبويب «التعارض» بأنواع المناوبات) — غياب
        // إحداهما لا يعطّل الأخرى.
        var rules = (await ShiftRuleStore.ListAsync(dbContext)).Where(r => r.IsActive).ToList();

        var days = await DayAttendanceStore.ListAsync(dbContext, scope, year, month, null);
        var shiftList = await ShiftTypeStore.ListAsync(dbContext);
        var shifts = shiftList.ToDictionary(s => s.Id, s => s.Days.ToDictionary(d => d.DayIndex));
        var shiftById = shiftList.ToDictionary(s => s.Id);
        var hasConflictShifts = shiftList.Any(s => s.ConflictLateReturnEnabled || s.ConflictEarlyLeaveEnabled);
        if (rules.Count == 0 && !hasConflictShifts) return (0, 0);

        // المفاتيح الموجودة مسبقاً (بأي حالة) — لمنع التكرار وإبقاء قرارات الفرز
        var existing = new HashSet<(int, DateOnly, int)>(
            (await ListAsync(dbContext, year, month, null))
                .Select(r => (r.EmployeeId, r.WorkDate, r.RuleId)));

        // المعلقة تُجمع لإدخال جماعي واحد (SqlBulkCopy)؛ التلقائية تُنفذ صفاً-بصف
        // لأن كلاً منها ينشئ قضية مخالفة مرتبطة (عادةً قليلة).
        var table = new DataTable();
        table.Columns.Add("EmployeeId", typeof(int));
        table.Columns.Add("WorkDate", typeof(DateTime));
        table.Columns.Add("RuleId", typeof(int));
        table.Columns.Add("RuleName", typeof(string));
        table.Columns.Add("Summary", typeof(string));
        table.Columns.Add("ActionType", typeof(string));
        table.Columns.Add("ActionText", typeof(string));
        table.Columns.Add("ActionValue", typeof(decimal));
        table.Columns.Add("Status", typeof(string));

        // عدّاد أزواج البصمات غير-الحضورية للشهر — يغذّي الشروط ذات النطاق
        // الدلالي («عدد المرات» لنوع بصمة). استعلام واحد بدل استعلام لكل يومية.
        var semanticCounts = await ShiftRuleStore.SemanticCountsAsync(dbContext, year, month);

        // عدّاد التكرار للقواعد ذات «تسلسل إجراءات تصاعدي»: كم مرّة سبق أن خالف هذا
        // الموظف هذه القاعدة **قبل** هذا الشهر ضمن نفس السنة (المتجاهَل لا يُحتسب —
        // تجاهُل المسؤول يعني أن المخالفة لم تقع). يُحمَّل باستعلام واحد ثم يُزاد داخل
        // الحلقة، فيصير رقم التكرار متسلسلاً عبر أيام الشهر أيضاً.
        var escalationCounts = await EscalationBaselineAsync(
            dbContext, year, month, rules.Where(r => r.UseEscalation).Select(r => r.Id).ToList());

        // **معايير الاستحقاق**: حقائق الموظف تُبنى مرّة واحدة لكل موظف (لا لكل يومية)
        // بمرجع زمني = آخر يوم بالشهر المُحلَّل، فتكون «مدة الخدمة» و«العمر» و«في فترة
        // التجربة» محسوبةً على نهاية الفترة لا على لحظة التشغيل. تُحمَّل فقط إن وُجدت
        // قاعدة واحدة ذات شروط — فلا كلفة على المستخدم الذي لا يستعملها.
        var facts = await EligibilityFactsAsync(dbContext, year, month, rules);

        int created = 0, auto = 0;
        foreach (var day in rules.Count == 0 ? new List<DayAttendanceStore.DayRow>() : days)
        {
            ShiftTypeStore.ShiftDay? shiftDay = null;
            if (day.ShiftTypeId.HasValue && shifts.TryGetValue(day.ShiftTypeId.Value, out var byIndex))
                byIndex.TryGetValue(DayAttendanceStore.ToDayIndex(day.WorkDate), out shiftDay);

            foreach (var rule in rules)
            {
                if (!ShiftRuleStore.AppliesTo(rule, day)) continue;
                if (!IsEligible(rule, day.EmployeeId, facts)) continue;
                if (existing.Contains((day.EmployeeId, day.WorkDate, rule.Id))) continue;

                var summary = ShiftRuleStore.Evaluate(rule, day, shiftDay, semanticCounts);
                if (summary == null) continue;

                // «تسلسل إجراءات تصاعدي»: رقم التكرار يُثبَّت بنصّ الاقتراح وبعنوان
                // الإجراء، فتلتقطه لائحة العقوبات المتدرّجة بمودل المخالفات (درجة العقوبة
                // عندها تُحدَّد بـOccurrenceFrom/OccurrenceTo). لا يمسّ القيمة المالية عمداً.
                var actionText = rule.ActionText;
                if (rule.UseEscalation)
                {
                    var key = (day.EmployeeId, rule.Id);
                    escalationCounts.TryGetValue(key, out var prior);
                    var occurrence = prior + 1;
                    escalationCounts[key] = occurrence;

                    summary = $"{summary} — التكرار رقم {occurrence}";
                    if (actionText is { Length: > 0 })
                        actionText = $"{actionText} (تكرار {occurrence})";
                }

                // اقتراح متعارض مع حركة قائمة (إجازة/عطلة) لا يُنفَّذ تلقائياً — للمراجعة اليدوية
                var conflict = IsConflict(rule, day);

                if (rule.IsAutomatic && !conflict)
                {
                    // الاقتراح أولاً (لمعرّفه الحقيقي) ثم تنفيذ الأثر ثم ربط معرّفاته
                    var recId = await HrmsDatabase.ScalarAsync<int>(
                        dbContext,
                        """
INSERT INTO AttendanceRecommendations
    (EmployeeId, WorkDate, RuleId, RuleName, Summary, ActionType, ActionText, ActionValue, Status, DecidedAt)
VALUES
    (@Employee, @Date, @Rule, @RuleName, @Summary, @ActionType, @ActionText, @ActionValue, N'Auto', SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                        command =>
                        {
                            HrmsDatabase.AddParameter(command, "@Employee", day.EmployeeId);
                            HrmsDatabase.AddParameter(command, "@Date", day.WorkDate.ToDateTime(TimeOnly.MinValue));
                            HrmsDatabase.AddParameter(command, "@Rule", rule.Id);
                            HrmsDatabase.AddParameter(command, "@RuleName", rule.Name);
                            HrmsDatabase.AddParameter(command, "@Summary", summary);
                            HrmsDatabase.AddParameter(command, "@ActionType", rule.ActionType);
                            HrmsDatabase.AddParameter(command, "@ActionText", actionText);
                            HrmsDatabase.AddParameter(command, "@ActionValue", rule.ActionValue);
                        });

                    var (violationId, transactionId) = await ExecuteEffectAsync(
                        dbContext, day.EmployeeId, day.WorkDate, recId, rule.Id, rule.Name,
                        rule.ActionType, actionText, rule.ActionValue, summary);

                    if (violationId.HasValue || transactionId.HasValue)
                        await HrmsDatabase.ExecuteAsync(
                            dbContext,
                            "UPDATE AttendanceRecommendations SET ViolationCaseId = @Violation, TransactionId = @Transaction WHERE Id = @Id;",
                            command =>
                            {
                                HrmsDatabase.AddParameter(command, "@Id", recId);
                                HrmsDatabase.AddParameter(command, "@Violation", (object?)violationId ?? DBNull.Value);
                                HrmsDatabase.AddParameter(command, "@Transaction", (object?)transactionId ?? DBNull.Value);
                            });
                    auto++;
                }
                else
                {
                    table.Rows.Add(day.EmployeeId, day.WorkDate.ToDateTime(TimeOnly.MinValue), rule.Id,
                        rule.Name, summary, rule.ActionType, actionText, rule.ActionValue,
                        conflict ? "Conflicted" : "Pending");
                }

                existing.Add((day.EmployeeId, day.WorkDate, rule.Id));
                created++;
            }
        }

        // ===== ممر تعارض المغادرات (تبويب «التعارض» بأنواع المناوبات) =====
        // مقارنة الحركة الفعلية (بصمات اليوم المصنّفة تناوباً) بنافذة المغادرة المعتمدة؛
        // كل تعارض = اقتراح Pending دائماً (لا تنفيذ تلقائي — العقوبة المالية بعين بشرية)،
        // بمعرّف قاعدة حارس سالب فلا يصطدم بقواعد AV بمفتاح منع التكرار.
        if (hasConflictShifts)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var permissions = await DayAttendanceStore.ApprovedPermissionsAsync(dbContext, monthStart, monthEnd);
            var punchTimes = permissions.Count == 0
                ? new Dictionary<(int, DateOnly), List<DateTime>>()
                : await MonthPunchTimesAsync(dbContext, monthStart, monthEnd);

            foreach (var day in days)
            {
                if (day.ShiftTypeId is not int sid || !shiftById.TryGetValue(sid, out var conflictShift)) continue;
                if (!conflictShift.ConflictLateReturnEnabled && !conflictShift.ConflictEarlyLeaveEnabled) continue;
                if (!permissions.TryGetValue((day.EmployeeId, day.WorkDate), out var dayPerms)) continue;
                if (!punchTimes.TryGetValue((day.EmployeeId, day.WorkDate), out var times) || times.Count == 0) continue;

                foreach (var (permStart, permEnd) in dayPerms)
                {
                    var (leftAt, returnAt) = SelectMovementAround(times, day.WorkDate.ToDateTime(permStart));
                    var window = $"{permStart:HH\\:mm}–{permEnd:HH\\:mm}";

                    if (conflictShift.ConflictEarlyLeaveEnabled && leftAt is { } left
                        && !existing.Contains((day.EmployeeId, day.WorkDate, ConflictEarlyLeaveRuleId)))
                    {
                        var result = MovementConflictPolicy.EvaluateEarlyLeave(
                            new MovementConflictPolicy.Rule(true,
                                conflictShift.ConflictEarlyLeaveAction, conflictShift.ConflictEarlyLeaveValue),
                            permStart, TimeOnly.FromDateTime(left));
                        if (result.Triggered)
                        {
                            table.Rows.Add(day.EmployeeId, day.WorkDate.ToDateTime(TimeOnly.MinValue),
                                ConflictEarlyLeaveRuleId, "تعارض المغادرة — خروج مبكّر",
                                $"خرج {left:HH\\:mm} قبل نافذة المغادرة المعتمدة {window} (تجاوز {result.MinutesOff} د)",
                                result.Action, ConflictActionText(result.Action), result.Value, "Pending");
                            existing.Add((day.EmployeeId, day.WorkDate, ConflictEarlyLeaveRuleId));
                            created++;
                        }
                    }

                    if (conflictShift.ConflictLateReturnEnabled && returnAt is { } returned
                        && !existing.Contains((day.EmployeeId, day.WorkDate, ConflictLateReturnRuleId)))
                    {
                        var result = MovementConflictPolicy.EvaluateLateReturn(
                            new MovementConflictPolicy.Rule(true,
                                conflictShift.ConflictLateReturnAction, conflictShift.ConflictLateReturnValue),
                            permEnd, TimeOnly.FromDateTime(returned));
                        if (result.Triggered)
                        {
                            table.Rows.Add(day.EmployeeId, day.WorkDate.ToDateTime(TimeOnly.MinValue),
                                ConflictLateReturnRuleId, "تعارض المغادرة — عودة متأخّرة",
                                $"عاد {returned:HH\\:mm} بعد نافذة المغادرة المعتمدة {window} (تجاوز {result.MinutesOff} د)",
                                result.Action, ConflictActionText(result.Action), result.Value, "Pending");
                            existing.Add((day.EmployeeId, day.WorkDate, ConflictLateReturnRuleId));
                            created++;
                        }
                    }
                }
            }
        }

        if (table.Rows.Count > 0)
        {
            var connection = (SqlConnection)dbContext.Database.GetDbConnection();
            var wasClosed = connection.State != ConnectionState.Open;
            if (wasClosed) await connection.OpenAsync();
            try
            {
                using var bulk = new SqlBulkCopy(connection);
                bulk.DestinationTableName = "AttendanceRecommendations";
                bulk.BulkCopyTimeout = 120;
                foreach (DataColumn column in table.Columns)
                {
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }
                await bulk.WriteToServerAsync(table);
            }
            finally
            {
                if (wasClosed) await connection.CloseAsync();
            }
        }
        return (created, auto);
    }

    /// <summary>
    /// تحديد حركة الموظف الفعلية حول نافذة مغادرة معتمدة من بصمات يومه المصنّفة تناوباً
    /// (<see cref="PunchTypingEngine"/>): المغادرة الفعلية = بصمة «خروج» الأقرب زمنياً
    /// لبداية النافذة، والعودة الفعلية = أول بصمة «دخول» بعدها. لا بصمة خروج = لا حركة
    /// تُقارن (يوم بلا مغادرة مرصودة). نقية قابلة للاختبار.
    /// </summary>
    public static (DateTime? ActualLeft, DateTime? ActualReturn) SelectMovementAround(
        IReadOnlyList<DateTime> dayTimes, DateTime approvedStart)
    {
        var typed = PunchTypingEngine.Derive(dayTimes);

        PunchTypingEngine.TypedPunch? left = null;
        foreach (var punch in typed)
        {
            if (punch.Type != "Out") continue;
            if (left == null
                || Math.Abs((punch.At - approvedStart).Ticks) < Math.Abs((left.At - approvedStart).Ticks))
            {
                left = punch;
            }
        }
        if (left == null) return (null, null);

        var returned = typed.FirstOrDefault(p => p.Type == "In" && p.At > left.At);
        return (left.At, returned?.At);
    }

    /// <summary>نصّ أثر التعارض بحسب الإجراء المُعدّ (يظهر بالاقتراح وبالحركة المالية).</summary>
    private static string ConflictActionText(string action) =>
        action == MovementConflictPolicy.ActionPermission
            ? "احتساب مغادرة إضافية (ساعات)"
            : "اقتطاع مبلغ من المسير";

    /// <summary>
    /// كل أوقات بصم الشهر (غير المحذوفة، بكل الدلالات — حركة المغادرة قد تكون بصمة
    /// غير-حضورية) مفهرسة موظف×يوم ومرتّبة. استعلام واحد للشهر كله.
    /// </summary>
    private static async Task<Dictionary<(int, DateOnly), List<DateTime>>> MonthPunchTimesAsync(
        ApplicationDbContext dbContext, DateOnly from, DateOnly to)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, AttendanceDate, CheckIn, CheckOut
FROM AttendanceRecords
WHERE AttendanceDate >= @From AND AttendanceDate <= @To AND ISNULL(IsDeleted, 0) = 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")
            });

        var result = new Dictionary<(int, DateOnly), List<DateTime>>();
        foreach (var row in rows)
        {
            var key = (row.EmployeeId, row.Date);
            if (!result.TryGetValue(key, out var list)) result[key] = list = new List<DateTime>();
            if (row.CheckIn is { } ci) list.Add(ci);
            if (row.CheckOut is { } co && (row.CheckIn is not { } c2 || co != c2)) list.Add(co);
        }
        foreach (var list in result.Values)
        {
            list.Sort();
        }
        return result;
    }

    public static Task<List<Recommendation>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string? status)
    {
        var from = new DateOnly(year, month, 1);
        return ListRangeAsync(dbContext, from, from.AddMonths(1).AddDays(-1), status);
    }

    /// <summary>
    /// اقتراحات مدىً صريح — تستعمله الشاشات التي تتبع **فترة غلق الحضور** (مثلاً
    /// 21 → 20) لا الشهر التقويمي: عرضُ شهرٍ تقويميّ بينما المسير يقرأ فترة الغلق
    /// يعني قراراً على أيامٍ خارج الفترة، وأيامَ فترةٍ لا تُعرض أصلاً.
    /// </summary>
    public static async Task<List<Recommendation>> ListRangeAsync(
        ApplicationDbContext dbContext, DateOnly from, DateOnly to, string? status)
    {
        await EnsureAsync(dbContext);

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, e.EmployeeNo, e.FullName
FROM AttendanceRecommendations r
INNER JOIN Employees e ON e.Id = r.EmployeeId
WHERE r.WorkDate >= @From AND r.WorkDate <= @To
ORDER BY r.WorkDate DESC, e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new Recommendation
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                Summary = HrmsDatabase.GetString(reader, "Summary"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                ActionValue = reader["ActionValue"] is decimal av ? av : 0,
                Status = HrmsDatabase.GetString(reader, "Status"),
                ViolationCaseId = HrmsDatabase.GetNullableInt(reader, "ViolationCaseId"),
                TransactionId = HrmsDatabase.GetNullableInt(reader, "TransactionId"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
            });

        return string.IsNullOrWhiteSpace(status)
            ? rows
            : rows.Where(r => r.Status == status).ToList();
    }

    /// <summary>
    /// اعتماد اقتراح معلق أو متعارض (تجاوز يدوي): ينفّذ الأثر — مخالفة ← قضية مرتبطة،
    /// إجازة/مغادرة/أوفرتايم/دخل/اقتطاع ← حركة AttendanceTransaction، ملاحظة ← بلا أثر.
    /// </summary>
    public static async Task<bool> ApproveAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int id)
    {
        await EnsureAsync(dbContext);

        // فحص النطاق داخل نفس الاستعلام: اقتراحُ موظفٍ خارج شركات المستخدم لا يُرجَع
        // صفّاً ⟹ لا اعتماد ولا أثر (قضية مخالفة/حركة مالية) عبر الشركات. غير المقيَّد = 1=1.
        var rec = (await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT TOP 1 r.*, e.EmployeeNo, e.FullName FROM AttendanceRecommendations r INNER JOIN Employees e ON e.Id = r.EmployeeId WHERE r.Id = @Id AND r.Status IN (N'Pending', N'Conflicted') AND {scope.ToSqlPredicate("e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => new Recommendation
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                Summary = HrmsDatabase.GetString(reader, "Summary"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                ActionValue = reader["ActionValue"] is decimal av ? av : 0
            })).FirstOrDefault();

        if (rec == null) return false;

        var (violationId, transactionId) = await ExecuteEffectAsync(
            dbContext, rec.EmployeeId, rec.WorkDate, rec.Id, rec.RuleId, rec.RuleName,
            rec.ActionType, rec.ActionText, rec.ActionValue, rec.Summary);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AttendanceRecommendations
SET Status = N'Approved', ViolationCaseId = @Violation, TransactionId = @Transaction, DecidedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Violation", (object?)violationId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Transaction", (object?)transactionId ?? DBNull.Value);
            });
        return true;
    }

    /// <summary>
    /// تنفيذ أثر القاعدة حسب نوعه: مخالفة ← قضية EmployeeViolationCase، الأنواع المالية/
    /// الإجازة/المغادرة ← حركة AttendanceTransaction، ملاحظة ← بلا أثر.
    /// </summary>
    /// <summary>
    /// حقائق «معايير الاستحقاق» لكل موظف — تُحمَّل مرّة واحدة لكل تحليل.
    /// تُرجع قاموساً فارغاً إن لم تكن أي قاعدة تحمل شروطاً (فلا استعلام بلا داعٍ).
    /// </summary>
    private static async Task<Dictionary<int, IReadOnlyDictionary<string, HrConditions.Fact>>>
        EligibilityFactsAsync(
            ApplicationDbContext dbContext, int year, int month,
            IReadOnlyCollection<ShiftRuleStore.ShiftRule> rules)
    {
        var map = new Dictionary<int, IReadOnlyDictionary<string, HrConditions.Fact>>();
        if (!rules.Any(r => !r.Conditions.IsEmpty)) return map;

        var asOf = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        foreach (var row in await HrConditionFacts.LoadAsync(dbContext))
            map[row.Id] = HrConditionFacts.Build(row, asOf);

        return map;
    }

    /// <summary>
    /// هل هذا الموظف مستحقّ لهذه القاعدة؟ **قاعدة بلا شروط تنطبق على الجميع**
    /// (<c>matchWhenEmpty: true</c>) — سلوك ما قبل الميزة، ونمط كيان: «إذا كانت هذه
    /// القاعدة مستحقّة لبعض الموظفين فقط». وموظف بلا حقائق (غير محمَّل) لا يُستثنى إلا
    /// إذا كانت القاعدة مشروطة فعلاً.
    /// </summary>
    private static bool IsEligible(
        ShiftRuleStore.ShiftRule rule, int employeeId,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, HrConditions.Fact>> facts)
    {
        var conditions = rule.Conditions;
        if (conditions.IsEmpty) return true;

        return facts.TryGetValue(employeeId, out var employeeFacts)
            && HrConditions.Matches(conditions, employeeFacts, matchWhenEmpty: true);
    }

    /// <summary>
    /// خطّ الأساس لعدّاد «تسلسل الإجراءات التصاعدي»: لكل (موظف × قاعدة) عددُ الاقتراحات
    /// السابقة **من بداية السنة حتى ما قبل الشهر المُحلَّل**، مستثنياً المتجاهَلة.
    /// استعلام واحد لكل التحليل بدل استعلام لكل يومية.
    /// </summary>
    private static async Task<Dictionary<(int EmployeeId, int RuleId), int>> EscalationBaselineAsync(
        ApplicationDbContext dbContext, int year, int month, IReadOnlyCollection<int> ruleIds)
    {
        var counts = new Dictionary<(int, int), int>();
        if (ruleIds.Count == 0) return counts;

        var yearStart = new DateTime(year, 1, 1);
        var monthStart = new DateTime(year, month, 1);
        var inList = string.Join(",", ruleIds.Select((_, i) => $"@R{i}"));

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT EmployeeId, RuleId, COUNT(1) AS Occurrences
FROM AttendanceRecommendations
WHERE RuleId IN ({inList})
  AND WorkDate >= @YearStart AND WorkDate < @MonthStart
  AND Status <> N'Ignored'
GROUP BY EmployeeId, RuleId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@YearStart", yearStart);
                HrmsDatabase.AddParameter(command, "@MonthStart", monthStart);
                var index = 0;
                foreach (var id in ruleIds)
                    HrmsDatabase.AddParameter(command, $"@R{index++}", id);
            },
            reader => (
                EmployeeId: HrmsDatabase.GetInt(reader, "EmployeeId"),
                RuleId: HrmsDatabase.GetInt(reader, "RuleId"),
                Occurrences: HrmsDatabase.GetInt(reader, "Occurrences")));

        foreach (var row in rows)
            counts[(row.EmployeeId, row.RuleId)] = row.Occurrences;

        return counts;
    }

    /// <summary>
    /// «تحليل القواعد الفترية» (نمط كيان — منشئ القواعد الشهرية/الأسبوعية): يقيّم شرائح
    /// <see cref="PeriodRuleStore"/> على التجميع الفتري ويحفظ مطابقاتها **كاقتراحات حضور**
    /// عادية، فتظهر بشاشة المتابعة وتمرّ بنفس دورة الاعتماد/التجاهل ونفس تنفيذ الأثر.
    ///
    /// <para>ثلاثة قرارات تصميم مقصودة:</para>
    /// <list type="number">
    /// <item>المعرّف يُخزَّن مُزاحاً وسالباً (<see cref="PeriodRuleStore.RecommendationRuleId"/>)
    /// فلا يصطدم بقواعد اليوميات الموجبة ولا بحارسَي التعارض (-1، -2).</item>
    /// <item>التاريخ = مرساة الفترة (آخر يوم بالشهر / أحد الأسبوع)، فيصير
    /// (موظف × فترة × قاعدة) مفتاحاً فريداً ⟹ إعادة التحليل لا تُكرّر ولا تُلغي فرزاً.</item>
    /// <item>الحالة **دائماً معلّقة** ولا تنفيذ تلقائي: القاعدة الفترية عقوبة متدرّجة
    /// على تجميع شهر كامل، فتُترك لعين بشرية — نفس مبدأ حارسَي التعارض.</item>
    /// </list>
    /// </summary>
    /// <returns>عدد الاقتراحات الجديدة.</returns>
    public static async Task<int> AnalyzePeriodAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, string periodType, int year, int period)
    {
        await EnsureAsync(dbContext);

        var matches = await PeriodRuleStore.EvaluateAsync(dbContext, scope, periodType, year, period);
        if (matches.Count == 0) return 0;

        var anchor = PeriodRuleStore.PeriodAnchorDate(periodType, year, period);
        var anchorDate = anchor.ToDateTime(TimeOnly.MinValue);

        var existing = new HashSet<(int, int)>(
            await HrmsDatabase.QueryAsync(
                dbContext,
                "SELECT EmployeeId, RuleId FROM AttendanceRecommendations WHERE WorkDate = @Date AND RuleId <= @Ceiling;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Date", anchorDate);
                    HrmsDatabase.AddParameter(command, "@Ceiling", -PeriodRuleStore.RuleIdOffset);
                },
                reader => (HrmsDatabase.GetInt(reader, "EmployeeId"), HrmsDatabase.GetInt(reader, "RuleId"))));

        var created = 0;
        foreach (var match in matches)
        {
            if (match.EmployeeId <= 0) continue;
            var ruleKey = PeriodRuleStore.RecommendationRuleId(match.RuleId);
            if (!existing.Add((match.EmployeeId, ruleKey))) continue;

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AttendanceRecommendations
    (EmployeeId, WorkDate, RuleId, RuleName, Summary, ActionType, ActionText, ActionValue, Status)
VALUES
    (@Employee, @Date, @Rule, @RuleName, @Summary, @ActionType, @ActionText, @ActionValue, N'Pending');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Employee", match.EmployeeId);
                    HrmsDatabase.AddParameter(command, "@Date", anchorDate);
                    HrmsDatabase.AddParameter(command, "@Rule", ruleKey);
                    HrmsDatabase.AddParameter(command, "@RuleName", match.RuleName);
                    HrmsDatabase.AddParameter(command, "@Summary", match.Summary);
                    HrmsDatabase.AddParameter(command, "@ActionType", match.ActionType);
                    HrmsDatabase.AddParameter(command, "@ActionText", match.ActionText);
                    HrmsDatabase.AddParameter(command, "@ActionValue", match.ActionValue);
                });
            created++;
        }

        return created;
    }

    private static async Task<(int? ViolationId, int? TransactionId)> ExecuteEffectAsync(
        ApplicationDbContext dbContext, int employeeId, DateOnly workDate, int recommendationId,
        int ruleId, string ruleName, string actionType, string actionText, decimal actionValue,
        string summary)
    {
        switch (actionType)
        {
            case "Note":
                return (null, null);

            case "Violation":
                var violationId = await CreateViolationAsync(dbContext, employeeId, workDate,
                    actionText, $"{ruleName}: {summary}");
                return (violationId, null);

            default: // Leave | Permission | Overtime | Income | Deduction
                var (amount, note) = await ResolveMoneyAsync(
                    dbContext, employeeId, workDate, ruleId, actionType, actionValue);

                var transactionId = await AttendanceTransactionStore.CreateAsync(dbContext,
                    employeeId, workDate, recommendationId, ruleId, ruleName,
                    actionType, string.IsNullOrEmpty(note) ? actionText : $"{actionText} ({note})", amount);
                return (null, transactionId);
        }
    }

    /// <summary>
    /// المبلغ الفعليّ للأثر الماليّ. القاعدة العادية تعطي مبلغاً ثابتاً؛ وقاعدة
    /// <see cref="ShiftRuleStore.PercentOfDayValueKind"/> تعطي **نسبةً من قيمة اليوم**
    /// تُحسب هنا — لحظة الاعتماد — من الراتب الأساسي ÷ ٣٠ (نفس مقام المسير).
    ///
    /// <para>راتبٌ مفقود يعني مبلغَ صفر لا خصماً عشوائياً: يُنشأ الأثر بقيمة صفر
    /// وبيانٍ يقول السبب، فيراه المستخدم بدل أن يخصم رقماً لا أساس له.</para>
    /// </summary>
    private static async Task<(decimal Amount, string Note)> ResolveMoneyAsync(
        ApplicationDbContext dbContext, int employeeId, DateOnly workDate, int ruleId,
        string actionType, decimal actionValue)
    {
        if (ruleId <= 0) return (actionValue, string.Empty);

        var rule = (await ShiftRuleStore.ListAsync(dbContext)).FirstOrDefault(r => r.Id == ruleId);
        if (rule is null || !ShiftRuleStore.IsPercentOfDay(rule.ValueKind, actionType))
            return (actionValue, string.Empty);

        // سلسلة التصاعد: النسبة تكبر بتكرار المخالفة. كان `UseEscalation` يكتب رقم
        // التكرار بالنصّ **ولا يمسّ المال** — أي إنذارٌ لفظيّ لا تصعيد. الدرجات
        // تُقرأ من أعمدة القاعدة القائمة (بلا عمودٍ جديد): الأولى `ActionValue`،
        // والثانية `ValueHours`، والثالثة فما فوق `ValueHours2` — وصفرٌ يعني «كما
        // سبقتها»، فقاعدةٌ بلا سلّم تبقى بنسبةٍ واحدة كما كانت.
        var percent = actionValue;
        var occurrenceNote = string.Empty;

        if (rule.UseEscalation)
        {
            var occurrence = await OccurrenceAsync(dbContext, employeeId, ruleId, workDate);
            var ladder = new[] { actionValue, rule.ValueHours, rule.ValueHours2 };

            var step = actionValue;
            for (var index = 0; index < ladder.Length && index < occurrence; index++)
            {
                if (ladder[index] > 0) step = ladder[index];
            }

            percent = step;
            occurrenceNote = $"التكرار {occurrence} · ";
        }

        var basic = await HrmsDatabase.ScalarAsync<decimal?>(
            dbContext,
            """
SELECT TOP 1 BasicSalary FROM EmployeeFinancialInfos
WHERE EmployeeId = @Employee AND ISNULL(IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Employee", employeeId)) ?? 0m;

        if (basic <= 0)
            return (0m, "بلا راتب أساسي مسجَّل — لم يُحتسب مبلغ");

        var dailyRate = WorkDaysBasis.DailyRate(basic, 30m);
        var amount = decimal.Round(dailyRate * percent / 100m, 2);

        return (amount, $"{occurrenceNote}{percent:0.##}% من قيمة اليوم {dailyRate:0.##}");
    }

    /// <summary>
    /// رقم تكرار هذه المخالفة لهذا الموظف حتى هذا اليوم (ضمن السنة). يُحسب **لحظة
    /// الاعتماد** لا وقت التحليل، فيوافق ما بُتَّ فيه فعلاً: المتجاهَل لا يُحتسب —
    /// تجاهُل المسؤول يعني أن المخالفة لم تقع، فلا تُصعِّد ما بعدها.
    /// </summary>
    private static async Task<int> OccurrenceAsync(
        ApplicationDbContext dbContext, int employeeId, int ruleId, DateOnly workDate)
    {
        var prior = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1) FROM AttendanceRecommendations
WHERE EmployeeId = @Employee AND RuleId = @Rule
  AND Status <> N'Ignored'
  AND WorkDate < @Date
  AND YEAR(WorkDate) = YEAR(@Date);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Rule", ruleId);
                HrmsDatabase.AddParameter(command, "@Date", workDate.ToDateTime(TimeOnly.MinValue));
            });

        return prior + 1;
    }

    public static async Task IgnoreAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int id)
    {
        await EnsureAsync(dbContext);
        // النطاق يُحقَن عبر EXISTS على الموظف: اقتراحُ شركةٍ أخرى لا يُلمَس. غير المقيَّد = 1=1.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"UPDATE r SET r.Status = N'Ignored', r.DecidedAt = SYSUTCDATETIME() FROM AttendanceRecommendations r INNER JOIN Employees e ON e.Id = r.EmployeeId WHERE r.Id = @Id AND r.Status IN (N'Pending', N'Conflicted') AND {scope.ToSqlPredicate("e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>
    /// تعديل الإجراء الموصى به **قبل اعتماده** — محكوم براية «السماح بتعديل الإجراء
    /// الموصى به» (<see cref="ShiftRuleStore.ShiftRule.AllowEdit"/>) على القاعدة المُصدِرة.
    ///
    /// <para>يُرفض التعديل إذا: الاقتراح ليس معلّقاً/متعارضاً (فالمعتمَد نفّذ أثره
    /// فعلاً)، أو القاعدة لا تسمح، أو كانت القاعدة فترية أو حارس تعارض (لا راية لها).</para>
    /// </summary>
    public static async Task<(bool Ok, string Message)> UpdateActionAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope,
        int id, string actionText, decimal actionValue)
    {
        await EnsureAsync(dbContext);

        // فحص النطاق ضمن نفس القراءة: اقتراحُ موظفٍ خارج شركات المستخدم لا يُرجَع ⟹
        // يُعامَل كغير موجود فلا يُعدَّل عبر الشركات. غير المقيَّد = 1=1.
        var row = (await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT TOP 1 r.RuleId, r.Status FROM AttendanceRecommendations r INNER JOIN Employees e ON e.Id = r.EmployeeId WHERE r.Id = @Id AND {scope.ToSqlPredicate("e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => (
                RuleId: HrmsDatabase.GetInt(reader, "RuleId"),
                Status: HrmsDatabase.GetString(reader, "Status")))).FirstOrDefault();

        if (row.Status is null)
            return (false, "لا يُعدَّل إلا اقتراح معلّق أو متعارض — المعتمَد نفّذ أثره.");

        if (row.Status is not ("Pending" or "Conflicted"))
            return (false, "لا يُعدَّل إلا اقتراح معلّق أو متعارض — المعتمَد نفّذ أثره.");

        if (row.RuleId <= 0)
            return (false, "هذا الاقتراح صادر عن قاعدة فترية أو حارس تعارض، ولا يقبل التعديل.");

        var rule = (await ShiftRuleStore.ListAsync(dbContext)).FirstOrDefault(r => r.Id == row.RuleId);
        if (rule is null)
            return (false, "القاعدة المُصدِرة غير موجودة.");
        if (!rule.AllowEdit)
            return (false, $"القاعدة «{rule.Name}» لا تسمح بتعديل الإجراء الموصى به.");

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE AttendanceRecommendations SET ActionText = @Text, ActionValue = @Value WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Text", actionText.Trim());
                HrmsDatabase.AddParameter(command, "@Value", actionValue);
            });

        return (true, "عُدِّل الإجراء الموصى به.");
    }

    /// <summary>معرّفات القواعد التي تسمح بتعديل إجرائها — لإظهار أدوات التعديل بالشاشة.</summary>
    public static async Task<HashSet<int>> EditableRuleIdsAsync(ApplicationDbContext dbContext) =>
        (await ShiftRuleStore.ListAsync(dbContext))
            .Where(r => r.AllowEdit)
            .Select(r => r.Id)
            .ToHashSet();

    /// <summary>إنشاء قضية مخالفة من الحضور — بمرجع AV مستقل ومصدر «محرك الحضور».</summary>
    private static async Task<int> CreateViolationAsync(ApplicationDbContext dbContext,
        int employeeId, DateOnly eventDate, string violationTitle, string notes)
    {
        await ViolationCaseSchema.EnsureAsync(dbContext);

        var prefix = $"AV{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(1) FROM EmployeeViolationCases WHERE ReferenceNo LIKE @Prefix;",
            command => HrmsDatabase.AddParameter(command, "@Prefix", prefix + "%"));

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO EmployeeViolationCases
    (ReferenceNo, EmployeeId, ViolationCategory, ViolationTitle, EventDate,
     Source, ActionStatus, Status, ProposedAction, Notes, CreatedAt, IsDeleted)
VALUES
    (@ReferenceNo, @EmployeeId, N'الحضور والانصراف', @Title, @EventDate,
     N'محرك الحضور', N'بانتظار الإجراء', N'مسودة', @Title, @Notes, SYSUTCDATETIME(), 0);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@ReferenceNo", $"{prefix}{count + 1:0000}");
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Title", violationTitle);
                HrmsDatabase.AddParameter(command, "@EventDate", eventDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Notes", notes);
            });
    }
}
