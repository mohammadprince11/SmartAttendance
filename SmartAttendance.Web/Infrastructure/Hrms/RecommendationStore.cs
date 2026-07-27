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
        ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);

        // قواعد AV + قواعد تعارض المغادرات (تبويب «التعارض» بأنواع المناوبات) — غياب
        // إحداهما لا يعطّل الأخرى.
        var rules = (await ShiftRuleStore.ListAsync(dbContext)).Where(r => r.IsActive).ToList();

        var days = await DayAttendanceStore.ListAsync(dbContext, year, month, null);
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

        int created = 0, auto = 0;
        foreach (var day in rules.Count == 0 ? new List<DayAttendanceStore.DayRow>() : days)
        {
            ShiftTypeStore.ShiftDay? shiftDay = null;
            if (day.ShiftTypeId.HasValue && shifts.TryGetValue(day.ShiftTypeId.Value, out var byIndex))
                byIndex.TryGetValue(DayAttendanceStore.ToDayIndex(day.WorkDate), out shiftDay);

            foreach (var rule in rules)
            {
                if (!ShiftRuleStore.AppliesTo(rule, day)) continue;
                if (existing.Contains((day.EmployeeId, day.WorkDate, rule.Id))) continue;

                var summary = ShiftRuleStore.Evaluate(rule, day, shiftDay, semanticCounts);
                if (summary == null) continue;

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
                            HrmsDatabase.AddParameter(command, "@ActionText", rule.ActionText);
                            HrmsDatabase.AddParameter(command, "@ActionValue", rule.ActionValue);
                        });

                    var (violationId, transactionId) = await ExecuteEffectAsync(
                        dbContext, day.EmployeeId, day.WorkDate, recId, rule.Id, rule.Name,
                        rule.ActionType, rule.ActionText, rule.ActionValue, summary);

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
                        rule.Name, summary, rule.ActionType, rule.ActionText, rule.ActionValue,
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

    public static async Task<List<Recommendation>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string? status)
    {
        await EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

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
    public static async Task<bool> ApproveAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);

        var rec = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 r.*, e.EmployeeNo, e.FullName FROM AttendanceRecommendations r INNER JOIN Employees e ON e.Id = r.EmployeeId WHERE r.Id = @Id AND r.Status IN (N'Pending', N'Conflicted');",
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
                var transactionId = await AttendanceTransactionStore.CreateAsync(dbContext,
                    employeeId, workDate, recommendationId, ruleId, ruleName,
                    actionType, actionText, actionValue);
                return (null, transactionId);
        }
    }

    public static async Task IgnoreAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE AttendanceRecommendations SET Status = N'Ignored', DecidedAt = SYSUTCDATETIME() WHERE Id = @Id AND Status IN (N'Pending', N'Conflicted');",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

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
