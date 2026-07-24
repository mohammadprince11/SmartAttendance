using SmartAttendance.Infrastructure.Persistence;

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
        ("EarlyLeaveHours", "إجمالي ساعات الخروج المبكر", true),
        ("WorkedHours", "إجمالي ساعات العمل", true),
        ("AbsentDays", "أيام الغياب", false),
        ("IncompleteDays", "أيام البصمة الناقصة", false),
        ("UnpaidLeaveDays", "أيام إجازة بدون راتب", false),
        ("PresentDays", "أيام الحضور", false)
    };

    public static readonly (string Key, string Label)[] ActionTypes =
    {
        ("Violation", "مخالفة"),
        ("Note", "ملاحظة (رصد فقط)"),
        ("Deduction", "اقتطاع")
    };

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
        public string Name { get; set; } = string.Empty;
        public string PeriodType { get; set; } = "Month";   // Month | Week
        public string Metric { get; set; } = "LateHours";
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
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

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
                Name = HrmsDatabase.GetString(reader, "Name"),
                PeriodType = HrmsDatabase.GetString(reader, "PeriodType") is { Length: > 0 } p ? p : "Month",
                Metric = HrmsDatabase.GetString(reader, "Metric") is { Length: > 0 } m ? m : "LateHours",
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
    public static async Task<(bool Ok, string Message)> SaveRuleAsync(ApplicationDbContext db, PeriodRule rule)
    {
        await EnsureAsync(db);
        if (string.IsNullOrWhiteSpace(rule.Name)) return (false, "اسم القاعدة مطلوب.");
        if (rule.Slices.Count == 0) return (false, "أضف شريحة واحدة على الأقل.");

        int ruleId;
        if (rule.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE PeriodRules SET Name=@Name, PeriodType=@Period, Metric=@Metric, IsActive=@Active WHERE Id=@Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", rule.Id);
                    HrmsDatabase.AddParameter(command, "@Name", rule.Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Period", rule.PeriodType);
                    HrmsDatabase.AddParameter(command, "@Metric", rule.Metric);
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
                "INSERT INTO PeriodRules (Name, PeriodType, Metric, IsActive) VALUES (@Name, @Period, @Metric, @Active); SELECT CAST(SCOPE_IDENTITY() AS int);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Name", rule.Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Period", rule.PeriodType);
                    HrmsDatabase.AddParameter(command, "@Metric", rule.Metric);
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
        return (true, rule.Id > 0 ? "تم تحديث القاعدة." : "أُنشئت القاعدة.");
    }

    public static async Task DeleteRuleAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM PeriodRuleSlices WHERE RuleId=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM PeriodRules WHERE Id=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    public sealed class Match
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public string MetricLabel { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string RangeText { get; set; } = string.Empty;
        public string ActionTypeLabel { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public decimal ActionValue { get; set; }
        public bool MetricIsHours { get; set; }
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
    public static async Task<List<Match>> EvaluateAsync(
        ApplicationDbContext db, string periodType, int year, int period)
    {
        var rules = (await ListRulesAsync(db))
            .Where(r => r.IsActive && r.PeriodType == periodType && r.Slices.Count > 0)
            .ToList();
        var result = new List<Match>();
        if (rules.Count == 0) return result;

        // (EmployeeNo, Name, metricAccessor)
        List<(string No, string Name, Func<string, decimal> Metric)> rows;
        if (periodType == "Week")
        {
            var weekRows = await WeekAttendanceStore.ListAsync(db, year, period);
            rows = weekRows.Select(w => (w.EmployeeNo, w.EmployeeName,
                (Func<string, decimal>)(key => WeekMetric(w, key)))).ToList();
        }
        else
        {
            var monthRows = await MonthAttendanceStore.ListAsync(db, year, period);
            rows = monthRows.Select(m => (m.EmployeeNo, m.EmployeeName,
                (Func<string, decimal>)(key => MonthMetric(m, key)))).ToList();
        }

        foreach (var rule in rules)
        {
            foreach (var row in rows)
            {
                var value = row.Metric(rule.Metric);
                var slice = MatchSlice(rule.Slices, value);
                if (slice == null) continue;
                result.Add(new Match
                {
                    EmployeeNo = row.No,
                    EmployeeName = row.Name,
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
