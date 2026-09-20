using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// القواعد الفترية (نمط كيان — قسم 36.هـ: منشئ القواعد الشهرية/الأسبوعية): قاعدة
/// تُقيَّم على تجميع فترة (شهر/أسبوع) لمقياس (تأخير/غياب/...) عبر <b>شرائح تصاعدية</b>
/// — كل شريحة مدى [من..إلى) بإجراء خاص، فينتج عقوبة متدرّجة (0-10س ← إنذار، 10-20 ←
/// خصم يوم، 20+ ← خصم يومين). التقييم يقرأ الحضور الشهري/الأسبوعي المُجمَّع ويطابق
/// قيمة كل موظف بشريحتها. self-healing (جدولان: قواعد + شرائح).
/// </summary>
public static class PeriodRuleStore
{
    public static readonly (string Key, string Label)[] PeriodTypes =
    {
        ("Month", "شهري"),
        ("Week", "أسبوعي (ISO)")
    };

    /// <summary>مقاييس التجميع المتاحة (من الحضور الشهري/الأسبوعي).</summary>
    public static readonly (string Key, string Label, bool IsHours)[] Metrics =
    {
        ("LateHours", "إجمالي ساعات التأخير", true),
        ("LateViolationDays", "أيام التأخير بعد استنفاد السماح", false),
        ("EarlyLeaveHours", "إجمالي ساعات الخروج المبكر", true),
        ("WorkedHours", "إجمالي ساعات العمل", true),
        ("AbsentDays", "أيام الغياب", false),
        ("ConsecutiveAbsentDays", "أطول غياب متواصل (أيام)", false),
        ("IncompleteDays", "أيام البصمة الناقصة", false),
        ("UnpaidLeaveDays", "أيام إجازة بدون راتب", false),
        ("PresentDays", "أيام الحضور", false)
    };

    /// <summary>
    /// المقاييس المحسوبة من اليوميات لا من صفّ التجميع — تحتاج قراءة أيام الفترة.
    /// </summary>
    public static bool IsDayDerivedMetric(string key) =>
        key is "ConsecutiveAbsentDays" or "LateViolationDays";

    public static bool AppliesToCompany(PeriodRule rule, int companyId) =>
        rule.CompanyId is null || rule.CompanyId == companyId;

    /// <summary>
    /// **أطول سلسلة غياب متواصل** داخل الفترة. دالة نقية على أيام مرتّبة تصاعدياً:
    /// يوم غائب يمدّ السلسلة، وأي يوم غير غائب يقطعها.
    ///
    /// <para>الفرق عن «أيام الغياب» جوهريّ قانونياً: خمسة أيام متفرّقة عبر الشهر ليست
    /// كخمسة أيام متتالية — والثانية وحدها تفتح باب الفصل بقانون العمل العراقي. ولهذا
    /// يفصلهما كيان بمقياسين («الغياب المتقطع» و«الغياب المتواصل»).</para>
    /// </summary>
    /// <param name="absenceByDate">أيام الفترة: التاريخ ⟵ هل هو غياب؟</param>
    public static int LongestAbsenceStreak(IEnumerable<(DateOnly Date, bool IsAbsent)> absenceByDate)
    {
        var longest = 0;
        var current = 0;
        DateOnly? previous = null;

        foreach (var (date, isAbsent) in absenceByDate.OrderBy(d => d.Date))
        {
            if (!isAbsent)
            {
                current = 0;
            }
            else
            {
                // فجوة بالتواريخ (يوم بلا يومية أصلاً) تقطع التواصل — لا تُفترَض استمراريةً.
                current = previous is { } prev && date.DayNumber == prev.DayNumber + 1
                    ? current + 1
                    : 1;
                if (current > longest) longest = current;
            }

            previous = date;
        }

        return longest;
    }

    /// <summary>
    /// فئات الأثر — **مطابِقة لقواعد اليوميات** (<see cref="ShiftRuleStore.ActionTypes"/>).
    /// كانت ثلاثاً فقط بلا سبب، فشريحة شهرية لم تكن تستطيع منح أوفرتايم أو خصم دخل
    /// بينما تستطيعه قاعدة يومية على نفس الموظف.
    /// </summary>
    public static readonly (string Key, string Label)[] ActionTypes = ShiftRuleStore.ActionTypes;

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    public static bool MetricIsHours(string key) =>
        Metrics.FirstOrDefault(m => m.Key == key).IsHours;

    public static string MetricLabel(string key) =>
        Metrics.FirstOrDefault(m => m.Key == key).Label ?? key;

    public sealed class Slice
    {
        public int Id { get; set; }
        public int RuleId { get; set; }
        public decimal SliceFrom { get; set; }
        public decimal? SliceTo { get; set; }               // null = ما فوق (∞)
        public string ActionType { get; set; } = "Violation";
        public string ActionText { get; set; } = string.Empty;
        public decimal ActionValue { get; set; }
        public int SortOrder { get; set; }

        public string RangeText => SliceTo.HasValue
            ? $"{SliceFrom:0.##} – {SliceTo:0.##}"
            : $"{SliceFrom:0.##}+";
    }

    public sealed class PeriodRule
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PeriodType { get; set; } = "Month";   // Month | Week
        public string Metric { get; set; } = "LateHours";
        public int AllowanceMinutes { get; set; }
        public bool IsActive { get; set; } = true;
        public List<Slice> Slices { get; set; } = new();

        public string PeriodLabel => LabelOf(PeriodTypes, PeriodType);
        public string MetricText => MetricLabel(Metric);
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('PeriodRules', 'U') IS NULL
BEGIN
    CREATE TABLE PeriodRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        PeriodType nvarchar(10) NOT NULL DEFAULT(N'Month'),
        Metric nvarchar(30) NOT NULL DEFAULT(N'LateHours'),
        AllowanceMinutes int NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        -- CompanyId يطابق عزل التهيئة؛ NULL يعني قاعدة مشتركة.
        CompanyId int NULL
    );
END;

IF OBJECT_ID('PeriodRules', 'U') IS NOT NULL
   AND COL_LENGTH('PeriodRules', 'AllowanceMinutes') IS NULL
    ALTER TABLE PeriodRules ADD AllowanceMinutes int NOT NULL
        CONSTRAINT DF_PeriodRules_AllowanceMinutes DEFAULT(0);

IF OBJECT_ID('PeriodRuleSlices', 'U') IS NULL
BEGIN
    CREATE TABLE PeriodRuleSlices
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RuleId int NOT NULL,
        SliceFrom decimal(9,2) NOT NULL DEFAULT(0),
        SliceTo decimal(9,2) NULL,
        ActionType nvarchar(20) NOT NULL DEFAULT(N'Violation'),
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        ActionValue decimal(12,2) NOT NULL DEFAULT(0),
        SortOrder int NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_PeriodRuleSlices_Rule ON PeriodRuleSlices (RuleId);
END;
""");
    }

    public static async Task<List<PeriodRule>> ListRulesAsync(ApplicationDbContext db)
    {
        await EnsureAsync(db);
        var rules = await HrmsDatabase.QueryAsync(
            db,
            "SELECT * FROM PeriodRules ORDER BY Name;",
            command => { },
            reader => new PeriodRule
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetNullableInt(reader, "CompanyId"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                PeriodType = HrmsDatabase.GetString(reader, "PeriodType") is { Length: > 0 } p ? p : "Month",
                Metric = HrmsDatabase.GetString(reader, "Metric") is { Length: > 0 } m ? m : "LateHours",
                AllowanceMinutes = Math.Max(0, HrmsDatabase.GetInt(reader, "AllowanceMinutes")),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });

        if (rules.Count == 0) return rules;

        var slices = await HrmsDatabase.QueryAsync(
            db,
            "SELECT * FROM PeriodRuleSlices ORDER BY RuleId, SortOrder, SliceFrom;",
            command => { },
            reader => new Slice
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                SliceFrom = reader["SliceFrom"] is decimal sf ? sf : 0,
                SliceTo = reader["SliceTo"] is decimal st ? st : null,
                ActionType = HrmsDatabase.GetString(reader, "ActionType") is { Length: > 0 } at ? at : "Violation",
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                ActionValue = reader["ActionValue"] is decimal av ? av : 0,
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
            });
        var byRule = slices.GroupBy(s => s.RuleId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var r in rules)
            r.Slices = byRule.TryGetValue(r.Id, out var list) ? list : new();
        return rules;
    }

    /// <summary>حفظ قاعدة مع شرائحها (استبدال كامل للشرائح).</summary>
    /// <summary>
    /// يحفظ القاعدة ويُرجع معرّفها مع النتيجة. <b>المعرّف لازم لا تجميليّ:</b> نسبة
    /// القاعدة الجديدة لشركة منشئها (عزل التهيئة — 8D–8M) بدونه تفشل **بصمت**
    /// فتبقى القاعدة مشتركة بين كل الشركات.
    /// </summary>
    public static async Task<(bool Ok, string Message, int RuleId)> SaveRuleAsync(ApplicationDbContext db, PeriodRule rule)
    {
        await EnsureAsync(db);
        if (string.IsNullOrWhiteSpace(rule.Name)) return (false, "اسم القاعدة مطلوب.", 0);
        if (rule.Slices.Count == 0) return (false, "أضف شريحة واحدة على الأقل.", 0);

        int ruleId;
        if (rule.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE PeriodRules SET Name=@Name, PeriodType=@Period, Metric=@Metric, AllowanceMinutes=@Allowance, IsActive=@Active WHERE Id=@Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", rule.Id);
                    HrmsDatabase.AddParameter(command, "@Name", rule.Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Period", rule.PeriodType);
                    HrmsDatabase.AddParameter(command, "@Metric", rule.Metric);
                    HrmsDatabase.AddParameter(command, "@Allowance", Math.Max(0, rule.AllowanceMinutes));
                    HrmsDatabase.AddParameter(command, "@Active", rule.IsActive ? 1 : 0);
                });
            ruleId = rule.Id;
            await HrmsDatabase.ExecuteAsync(db, "DELETE FROM PeriodRuleSlices WHERE RuleId=@Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", ruleId));
        }
        else
        {
            ruleId = await HrmsDatabase.ScalarAsync<int>(
                db,
                "INSERT INTO PeriodRules (Name, PeriodType, Metric, AllowanceMinutes, IsActive) VALUES (@Name, @Period, @Metric, @Allowance, @Active); SELECT CAST(SCOPE_IDENTITY() AS int);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Name", rule.Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Period", rule.PeriodType);
                    HrmsDatabase.AddParameter(command, "@Metric", rule.Metric);
                    HrmsDatabase.AddParameter(command, "@Allowance", Math.Max(0, rule.AllowanceMinutes));
                    HrmsDatabase.AddParameter(command, "@Active", rule.IsActive ? 1 : 0);
                });
        }

        var order = 0;
        foreach (var s in rule.Slices.OrderBy(x => x.SliceFrom))
        {
            var current = s;
            var idx = order++;
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO PeriodRuleSlices (RuleId, SliceFrom, SliceTo, ActionType, ActionText, ActionValue, SortOrder)
VALUES (@Rule, @From, @To, @AType, @AText, @AValue, @Sort);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Rule", ruleId);
                    HrmsDatabase.AddParameter(command, "@From", current.SliceFrom);
                    HrmsDatabase.AddParameter(command, "@To", (object?)current.SliceTo ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@AType", current.ActionType);
                    HrmsDatabase.AddParameter(command, "@AText", current.ActionText ?? string.Empty);
                    HrmsDatabase.AddParameter(command, "@AValue", current.ActionValue);
                    HrmsDatabase.AddParameter(command, "@Sort", idx);
                });
        }
        return (true, rule.Id > 0 ? "تم تحديث القاعدة." : "أُنشئت القاعدة.", ruleId);
    }

    public static async Task DeleteRuleAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM PeriodRuleSlices WHERE RuleId=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM PeriodRules WHERE Id=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>
    /// فضاء أسماء معرّفات القواعد الفترية داخل <c>AttendanceRecommendations.RuleId</c>.
    /// مفتاح منع التكرار هناك هو (EmployeeId, WorkDate, RuleId)، وقواعد اليوميات تستهلك
    /// المعرّفات الموجبة (IDENTITY) بينما حارسا التعارض يستهلكان -1 و-2. فلكي لا تصطدم
    /// القاعدة الفترية رقم 1 بقاعدة اليوميات رقم 1، تُطرح بإزاحة كبيرة.
    /// </summary>
    public const int RuleIdOffset = 1000;

    /// <summary>معرّف القاعدة الفترية كما يُخزَّن باقتراحات الحضور (سالب ومُزاح).</summary>
    public static int RecommendationRuleId(int periodRuleId) => -(RuleIdOffset + periodRuleId);

    /// <summary>هل هذا المعرّف عائد لقاعدة فترية؟</summary>
    public static bool IsPeriodRuleId(int recommendationRuleId) =>
        recommendationRuleId <= -RuleIdOffset;

    /// <summary>
    /// تاريخ مرساة الفترة — يوم واحد يمثّل الفترة كلها باقتراح الحضور (آخر يوم بالشهر،
    /// أو أحد الأسبوع بترقيم ISO). يجعل (موظف × فترة × قاعدة) مفتاحاً فريداً.
    /// </summary>
    public static DateOnly PeriodAnchorDate(string periodType, int year, int period)
    {
        if (periodType == "Week")
        {
            // ISO: الأسبوع 1 هو الذي يحوي أول خميس؛ المرساة = أحد ذلك الأسبوع.
            var jan4 = new DateOnly(year, 1, 4);
            var isoMonday = jan4.AddDays(-((int)jan4.DayOfWeek + 6) % 7);
            return isoMonday.AddDays((period - 1) * 7 + 6);
        }

        var monthStart = new DateOnly(year, Math.Clamp(period, 1, 12), 1);
        return monthStart.AddMonths(1).AddDays(-1);
    }

    public sealed class Match
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string MetricLabel { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string RangeText { get; set; } = string.Empty;
        public string ActionType { get; set; } = "Violation";
        public string ActionTypeLabel { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public decimal ActionValue { get; set; }
        public bool MetricIsHours { get; set; }

        /// <summary>جملة الاقتراح كما تُخزَّن بملخّص اقتراح الحضور.</summary>
        public string Summary =>
            $"{MetricLabel} = {Value:0.##}{(MetricIsHours ? " ساعة" : " يوم")} ضمن الشريحة {RangeText}";
    }

    /// <summary>الشريحة المطابِقة لقيمة (SliceFrom ≤ value &lt; SliceTo؛ الأعلى فأعلى تفوز عند التداخل). دالة نقية.</summary>
    public static Slice? MatchSlice(IEnumerable<Slice> slices, decimal value)
    {
        // ترتيب تنازلي بـSliceFrom: أول شريحة حدّها الأدنى ≤ القيمة وحدّها الأعلى يسعها.
        foreach (var s in slices.OrderByDescending(x => x.SliceFrom))
        {
            if (value >= s.SliceFrom && (s.SliceTo == null || value < s.SliceTo.Value))
                return s;
        }
        return null;
    }

    /// <summary>
    /// تقييم القواعد الفترية على فترة مُحدَّدة: يقرأ الحضور المُجمَّع (شهري/أسبوعي)
    /// ويطابق قيمة كل موظف بشريحتها، فينتج قائمة العقوبات المتدرّجة المُقترحة.
    /// </summary>
    /// <param name="scope">نطاق الشركات — إلزامي، يسري على اليوميات المقروءة فلا
    /// تُقترح إجراءات (سلسلة التصاعد تمسّ المال) على موظفي شركات أخرى.</param>
    public static async Task<List<Match>> EvaluateAsync(
        ApplicationDbContext db, CompanyScope scope, string periodType, int year, int period)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Match>();

        var allowedRuleIds = scope.IsUnrestricted
            ? null
            : await ConfigTenantScope.AllowedIdsAsync(db, ConfigTenantScope.PeriodRules, scope);

        var rules = (await ListRulesAsync(db))
            .Where(r => r.IsActive
                        && r.PeriodType == periodType
                        && r.Slices.Count > 0
                        && (scope.IsUnrestricted || allowedRuleIds!.Contains(r.Id)))
            .ToList();
        var result = new List<Match>();
        if (rules.Count == 0) return result;

        // (EmployeeId, CompanyId, EmployeeNo, Name, metricAccessor)
        List<(int Id, int CompanyId, string No, string Name, Func<string, decimal> Metric)> rows;
        if (periodType == "Week")
        {
            var weekRows = await WeekAttendanceStore.ListAsync(db, scope, year, period);
            rows = weekRows.Select(w => (w.EmployeeId, w.CompanyId, w.EmployeeNo, w.EmployeeName,
                (Func<string, decimal>)(key => WeekMetric(w, key)))).ToList();
        }
        else
        {
            var monthRows = await MonthAttendanceStore.ListAsync(db, scope, year, period);
            rows = monthRows.Select(m => (m.EmployeeId, m.CompanyId, m.EmployeeNo, m.EmployeeName,
                (Func<string, decimal>)(key => MonthMetric(m, key)))).ToList();
        }

        var needsDayMetrics = rules.Any(r => IsDayDerivedMetric(r.Metric));
        var dayCache = new Dictionary<int, Dictionary<int, List<DayAttendanceStore.DayRow>>>();

        async Task<IReadOnlyList<DayAttendanceStore.DayRow>> DaysForAsync(int companyId, int employeeId)
        {
            if (!needsDayMetrics) return Array.Empty<DayAttendanceStore.DayRow>();

            if (!dayCache.TryGetValue(companyId, out var byEmployee))
            {
                DateOnly from;
                DateOnly to;
                if (periodType == "Week")
                {
                    (from, to) = WeekAttendanceStore.WeekRange(year, period);
                }
                else
                {
                    var (attendancePeriod, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(
                        db, year, period, SmartAttendance.Domain.Enums.PayrollCutoffType.Attendance, companyId);
                    from = attendancePeriod.From;
                    to = attendancePeriod.To;
                }

                var companyDays = await DayAttendanceStore.ListRangeAsync(
                    db, CompanyScope.ForCompanies(new[] { companyId }), from, to, null, computeStale: false);
                byEmployee = companyDays.GroupBy(day => day.EmployeeId)
                    .ToDictionary(group => group.Key, group => group.ToList());
                dayCache[companyId] = byEmployee;
            }

            return byEmployee.TryGetValue(employeeId, out var employeeDays)
                ? employeeDays
                : Array.Empty<DayAttendanceStore.DayRow>();
        }

        foreach (var rule in rules)
        {
            foreach (var row in rows)
            {
                if (!AppliesToCompany(rule, row.CompanyId))
                    continue;

                decimal value;
                if (rule.Metric == "ConsecutiveAbsentDays")
                {
                    var days = await DaysForAsync(row.CompanyId, row.Id);
                    value = LongestAbsenceStreak(days.Select(day => (day.WorkDate, day.EffectiveStatus == "Absent")));
                }
                else if (rule.Metric == "LateViolationDays")
                {
                    var days = await DaysForAsync(row.CompanyId, row.Id);
                    value = CountLateViolationDays(days, rule.AllowanceMinutes);
                }
                else
                {
                    value = row.Metric(rule.Metric);
                }
                var slice = MatchSlice(rule.Slices, value);
                if (slice == null) continue;
                result.Add(new Match
                {
                    EmployeeId = row.Id,
                    EmployeeNo = row.No,
                    EmployeeName = row.Name,
                    RuleId = rule.Id,
                    ActionType = slice.ActionType,
                    RuleName = rule.Name,
                    MetricLabel = rule.MetricText,
                    Value = value,
                    RangeText = slice.RangeText,
                    ActionTypeLabel = LabelOf(ActionTypes, slice.ActionType),
                    ActionText = slice.ActionText,
                    ActionValue = slice.ActionValue,
                    MetricIsHours = MetricIsHours(rule.Metric)
                });
            }
        }
        return result.OrderBy(r => r.RuleName).ThenByDescending(r => r.Value).ToList();
    }

    public static int CountLateViolationDays(
        IEnumerable<DayAttendanceStore.DayRow> days,
        int allowanceMinutes)
    {
        ArgumentNullException.ThrowIfNull(days);

        var lateDays = days.Select(day => new AttendanceLateAllowancePolicy.LateDay(
            day.WorkDate,
            Math.Max(0, (int)Math.Round(day.LateHours * 60m, MidpointRounding.AwayFromZero))));

        return AttendanceLateAllowancePolicy.Evaluate(lateDays, allowanceMinutes)
            .Count(result => result.IsViolationDay);
    }

    private static decimal MonthMetric(MonthAttendanceStore.MonthRow m, string key) => key switch
    {
        "LateHours" => m.LateHours,
        "EarlyLeaveHours" => m.EarlyLeaveHours,
        "WorkedHours" => m.WorkedHours,
        "AbsentDays" => m.AbsentDays,
        "IncompleteDays" => m.IncompleteDays,
        "UnpaidLeaveDays" => m.UnpaidLeaveDays,
        "PresentDays" => m.PresentDays,
        _ => 0
    };

    private static decimal WeekMetric(WeekAttendanceStore.WeekRow w, string key) => key switch
    {
        "LateHours" => w.LateHours,
        "EarlyLeaveHours" => w.EarlyLeaveHours,
        "WorkedHours" => w.WorkedHours,
        "AbsentDays" => w.AbsentDays,
        "IncompleteDays" => w.IncompleteDays,
        "UnpaidLeaveDays" => w.UnpaidLeaveDays,
        "PresentDays" => w.PresentDays,
        _ => 0
    };
}
