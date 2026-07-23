using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قواعد المناوبات (نمط كيان — قسم 10 بدراسة الحضور): القاعدة = نطاق (مناوبات ×
/// سياق يوم ×معايير استحقاق × أيام أسبوع) + شرط (نوع بصمة × حقل يومية × مقارنة ×
/// قيمة زمنية مطلقة/نسبية بمرساة يوم — تدعم «بين») + أثر (نوعه أحد ستة: مخالفة/
/// ملاحظة/إجازة/مغادرة/أوفرتايم/دخل/اقتطاع، مع قيمة عددية، تلقائي؟ قابل للتعديل؟).
/// التقييم يمر على يوميات DayAttendances ويولّد اقتراحات AttendanceRecommendations
/// (راجع RecommendationStore). الأثر غير-المخالفة يُنفَّذ كحركة AttendanceTransaction
/// (تُغذّي الرواتب لاحقاً).
/// </summary>
public static class ShiftRuleStore
{
    /// <summary>حقول الشرط (دلالات كيان المتاحة بيومياتنا) — المفتاح ← التسمية.</summary>
    public static readonly (string Key, string Label)[] ConditionFields =
    {
        ("CheckIn", "ختم الدخول"),
        ("CheckOut", "ختم الخروج"),
        ("Duration", "المدة (ساعات العمل)"),
        ("LateHours", "ساعات التأخير"),
        ("EarlyLeaveHours", "ساعات الخروج المبكر"),
        ("Absent", "غائب"),
        ("MissingCheckOut", "ختم الخروج مفقود"),
        // يُقيَّم على أزواج نوع البصمة المختار (استراحة/صلاة...) — مثال كيان الحيّ:
        // «استراحة عدد المرات أكثر من 3 ← سلوك سيء».
        ("PunchCount", "عدد المرات (لنوع البصمة)")
    };

    /// <summary>هل الحقل عددي (يقارَن بـأكثر/أقل/بين) لا زمني؟</summary>
    public static bool IsNumericField(string field) =>
        field is "Duration" or "LateHours" or "EarlyLeaveHours" or "PunchCount";

    /// <summary>هل الحقل يُقيَّم على أزواج نوع بصمة بعينه لا على اليومية؟</summary>
    public static bool IsSemanticScopedField(string field) =>
        field is "PunchCount";

    /// <summary>
    /// يبني عدّاد أزواج البصمات غير-الحضورية لشهر كامل (موظف × يوم × دلالة).
    /// استعلام واحد للشهر بدل استعلام لكل يومية.
    /// </summary>
    public static async Task<SemanticCounts> SemanticCountsAsync(
        ApplicationDbContext dbContext, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var counts = new SemanticCounts();

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, AttendanceDate, PunchSemanticId, COUNT(*) AS PunchCount
FROM AttendanceRecords
WHERE ISNULL(IsDeleted, 0) = 0
  AND AttendanceDate >= @From AND AttendanceDate <= @To
  AND PunchSemanticId IS NOT NULL
GROUP BY EmployeeId, AttendanceDate, PunchSemanticId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from);
                HrmsDatabase.AddParameter(command, "@To", to);
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                SemanticId = HrmsDatabase.GetInt(reader, "PunchSemanticId"),
                Count = HrmsDatabase.GetInt(reader, "PunchCount")
            });

        foreach (var row in rows)
        {
            counts.Add(row.EmployeeId, row.Date, row.SemanticId, row.Count);
        }

        return counts;
    }

    public static readonly (string Key, string Label)[] Comparisons =
    {
        ("Before", "قبل"),
        ("BeforeOrAt", "قبل/في"),
        ("After", "بعد"),
        ("AfterOrAt", "بعد/في"),
        ("Between", "بين"),
        ("MoreThan", "أكثر من"),
        ("LessThan", "أقل من")
    };

    public static readonly (string Key, string Label)[] ValueKinds =
    {
        ("Time", "وقت محدد"),
        ("ShiftStart", "وقت بدء المناوبة"),
        ("ShiftEnd", "وقت انتهاء المناوبة"),
        ("Hours", "عدد ساعات")
    };

    /// <summary>مرساة اليوم للقيمة الزمنية — حل عبور منتصف الليل (نمط كيان).</summary>
    public static readonly (string Key, string Label)[] DayAnchors =
    {
        ("Same", "نفس اليوم"),
        ("Prev", "اليوم السابق"),
        ("Next", "اليوم التالي")
    };

    /// <summary>معايير الاستحقاق «طبّق على» — سياق اليوم (7 قيم نمط كيان).</summary>
    public static readonly (string Key, string Label)[] ApplyOnOptions =
    {
        ("All", "كل الأيام"),
        ("Work", "أيام العمل"),
        ("Weekend", "أيام العطلة"),
        ("Rest", "أيام الراحة"),
        ("Holiday", "العطل الرسمية"),
        ("Leave", "أيام الإجازة"),
        ("BusinessTrip", "رحلات العمل")
    };

    /// <summary>نوع الأثر (6 أنواع كيان + ملاحظة رصد) — يحدد كيف يُنفَّذ الاقتراح.</summary>
    public static readonly (string Key, string Label)[] ActionTypes =
    {
        ("Violation", "مخالفة"),
        ("Note", "ملاحظة (رصد فقط)"),
        ("Leave", "إجازة"),
        ("Permission", "مغادرة"),
        ("Overtime", "عمل إضافي (أوفرتايم)"),
        ("Income", "دخل"),
        ("Deduction", "اقتطاع")
    };

    /// <summary>هل يحمل نوع الأثر قيمة عددية (مبلغ/ساعات)؟</summary>
    public static bool ActionNeedsValue(string actionType) =>
        actionType is "Overtime" or "Income" or "Deduction" or "Permission";

    /// <summary>وحدة القيمة العددية للأثر (ساعات للأوفرتايم/المغادرة، مبلغ للدخل/الاقتطاع).</summary>
    public static string ActionValueUnit(string actionType) =>
        actionType is "Overtime" or "Permission" ? "ساعة" : "مبلغ";

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    public sealed class ShiftRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShiftTypeIds { get; set; } = string.Empty;   // CSV، فارغ = كل المناوبات
        public string ApplyOn { get; set; } = "Work";              // معايير الاستحقاق — سياق اليوم
        public string WeekDays { get; set; } = string.Empty;       // CSV مؤشرات 0..6، فارغ = الكل
        public int? PunchSemanticId { get; set; }                  // نوع البصمة، null = الحضور/الكل
        public string ConditionField { get; set; } = "CheckIn";
        public string Comparison { get; set; } = "After";
        public string ValueKind { get; set; } = "Time";
        public string? ValueTime { get; set; }                     // HH:mm للطرف الأول
        public string ValueAnchor { get; set; } = "Same";          // مرساة يوم الطرف الأول
        public string? ValueTime2 { get; set; }                    // HH:mm للطرف الثاني («بين»)
        public string ValueAnchor2 { get; set; } = "Same";         // مرساة يوم الطرف الثاني
        public int OffsetMinutes { get; set; }                     // للنسبي: + بعد المرجع، - قبله
        public decimal ValueHours { get; set; }
        public decimal ValueHours2 { get; set; }                   // الطرف الثاني للساعات («بين»)
        public string ActionType { get; set; } = "Violation";      // انظر ActionTypes
        public string ActionText { get; set; } = string.Empty;
        public decimal ActionValue { get; set; }                   // مبلغ/ساعات الأثر
        public bool AllowEdit { get; set; }                        // السماح بتعديل الإجراء الموصى به
        public bool UseEscalation { get; set; }                    // تسلسل إجراءات تصاعدي
        public bool IsAutomatic { get; set; }
        public bool IsActive { get; set; } = true;

        public List<int> ShiftTypeIdList =>
            ShiftTypeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();

        public List<int> WeekDayList =>
            WeekDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v is >= 0 and <= 6).ToList();

        public bool IsHoursField => ConditionField is "Duration" or "LateHours" or "EarlyLeaveHours";

        /// <summary>جملة القاعدة بأسلوب كيان: «في حالة [حقل] [شرط] [قيمة] ← [إجراء]».</summary>
        public string Sentence
        {
            get
            {
                var fieldLabel = LabelOf(ConditionFields, ConditionField);
                var actionLabel = ActionText is { Length: > 0 } ? ActionText : LabelOf(ActionTypes, ActionType);
                if (ConditionField is "Absent" or "MissingCheckOut")
                    return $"في حالة {fieldLabel} ← {actionLabel}";

                string ValuePart(string? time, decimal hours, string anchor)
                {
                    if (IsHoursField) return $"{hours:0.##} ساعة";
                    if (ValueKind == "Time") return time ?? "؟";
                    var anchorSuffix = anchor == "Same" ? "" : $" ({LabelOf(DayAnchors, anchor)})";
                    return LabelOf(ValueKinds, ValueKind) + OffsetMinutes switch
                    {
                        > 0 => $" +{OffsetMinutes}د",
                        < 0 => $" -{-OffsetMinutes}د",
                        _ => ""
                    } + anchorSuffix;
                }

                var value = Comparison == "Between"
                    ? $"{ValuePart(ValueTime, ValueHours, ValueAnchor)} و{ValuePart(ValueTime2, ValueHours2, ValueAnchor2)}"
                    : ValuePart(ValueTime, ValueHours, ValueAnchor);

                return $"في حالة {fieldLabel} {LabelOf(Comparisons, Comparison)} {value} ← {actionLabel}";
            }
        }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ShiftRules', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        ShiftTypeIds nvarchar(200) NOT NULL DEFAULT(N''),
        ApplyOn nvarchar(20) NOT NULL DEFAULT(N'Work'),
        WeekDays nvarchar(30) NOT NULL DEFAULT(N''),
        PunchSemanticId int NULL,
        ConditionField nvarchar(30) NOT NULL,
        Comparison nvarchar(20) NOT NULL DEFAULT(N'After'),
        ValueKind nvarchar(20) NOT NULL DEFAULT(N'Time'),
        ValueTime nvarchar(5) NULL,
        ValueAnchor nvarchar(10) NOT NULL DEFAULT(N'Same'),
        ValueTime2 nvarchar(5) NULL,
        ValueAnchor2 nvarchar(10) NOT NULL DEFAULT(N'Same'),
        OffsetMinutes int NOT NULL DEFAULT(0),
        ValueHours decimal(5,2) NOT NULL DEFAULT(0),
        ValueHours2 decimal(5,2) NOT NULL DEFAULT(0),
        ActionType nvarchar(20) NOT NULL DEFAULT(N'Violation'),
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        ActionValue decimal(12,2) NOT NULL DEFAULT(0),
        AllowEdit bit NOT NULL DEFAULT(0),
        UseEscalation bit NOT NULL DEFAULT(0),
        IsAutomatic bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- ترقية تدريجية للأعمدة المضافة (نمط كيان: معايير استحقاق + بين + مرساة + أثر مالي)
IF COL_LENGTH('ShiftRules', 'PunchSemanticId') IS NULL ALTER TABLE ShiftRules ADD PunchSemanticId int NULL;
IF COL_LENGTH('ShiftRules', 'ValueAnchor') IS NULL ALTER TABLE ShiftRules ADD ValueAnchor nvarchar(10) NOT NULL DEFAULT(N'Same');
IF COL_LENGTH('ShiftRules', 'ValueTime2') IS NULL ALTER TABLE ShiftRules ADD ValueTime2 nvarchar(5) NULL;
IF COL_LENGTH('ShiftRules', 'ValueAnchor2') IS NULL ALTER TABLE ShiftRules ADD ValueAnchor2 nvarchar(10) NOT NULL DEFAULT(N'Same');
IF COL_LENGTH('ShiftRules', 'ValueHours2') IS NULL ALTER TABLE ShiftRules ADD ValueHours2 decimal(5,2) NOT NULL DEFAULT(0);
IF COL_LENGTH('ShiftRules', 'ActionValue') IS NULL ALTER TABLE ShiftRules ADD ActionValue decimal(12,2) NOT NULL DEFAULT(0);
IF COL_LENGTH('ShiftRules', 'AllowEdit') IS NULL ALTER TABLE ShiftRules ADD AllowEdit bit NOT NULL DEFAULT(0);
IF COL_LENGTH('ShiftRules', 'UseEscalation') IS NULL ALTER TABLE ShiftRules ADD UseEscalation bit NOT NULL DEFAULT(0);
""");
    }

    public static async Task<List<ShiftRule>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftRules ORDER BY Name;",
            command => { },
            reader => new ShiftRule
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                ShiftTypeIds = HrmsDatabase.GetString(reader, "ShiftTypeIds"),
                ApplyOn = HrmsDatabase.GetString(reader, "ApplyOn") is { Length: > 0 } a ? a : "Work",
                WeekDays = HrmsDatabase.GetString(reader, "WeekDays"),
                PunchSemanticId = HrmsDatabase.GetNullableInt(reader, "PunchSemanticId"),
                ConditionField = HrmsDatabase.GetString(reader, "ConditionField"),
                Comparison = HrmsDatabase.GetString(reader, "Comparison"),
                ValueKind = HrmsDatabase.GetString(reader, "ValueKind"),
                ValueTime = HrmsDatabase.GetString(reader, "ValueTime") is { Length: > 0 } t ? t : null,
                ValueAnchor = HrmsDatabase.GetString(reader, "ValueAnchor") is { Length: > 0 } an ? an : "Same",
                ValueTime2 = HrmsDatabase.GetString(reader, "ValueTime2") is { Length: > 0 } t2 ? t2 : null,
                ValueAnchor2 = HrmsDatabase.GetString(reader, "ValueAnchor2") is { Length: > 0 } an2 ? an2 : "Same",
                OffsetMinutes = HrmsDatabase.GetInt(reader, "OffsetMinutes"),
                ValueHours = reader["ValueHours"] is decimal h ? h : 0,
                ValueHours2 = reader["ValueHours2"] is decimal h2 ? h2 : 0,
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                ActionValue = reader["ActionValue"] is decimal av ? av : 0,
                AllowEdit = HrmsDatabase.GetBool(reader, "AllowEdit"),
                UseEscalation = HrmsDatabase.GetBool(reader, "UseEscalation"),
                IsAutomatic = HrmsDatabase.GetBool(reader, "IsAutomatic"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, ShiftRule rule)
    {
        await EnsureAsync(dbContext);

        if (rule.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE ShiftRules
SET Name = @Name, ShiftTypeIds = @Shifts, ApplyOn = @ApplyOn, WeekDays = @Days,
    PunchSemanticId = @Semantic, ConditionField = @Field, Comparison = @Cmp, ValueKind = @Kind,
    ValueTime = @Time, ValueAnchor = @Anchor, ValueTime2 = @Time2, ValueAnchor2 = @Anchor2,
    OffsetMinutes = @Offset, ValueHours = @Hours, ValueHours2 = @Hours2, ActionType = @ActionType,
    ActionText = @ActionText, ActionValue = @ActionValue, AllowEdit = @AllowEdit,
    UseEscalation = @Escalation, IsAutomatic = @Auto, IsActive = @Active
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", rule.Id);
                    AddParameters(command, rule);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ShiftRules
    (Name, ShiftTypeIds, ApplyOn, WeekDays, PunchSemanticId, ConditionField, Comparison, ValueKind,
     ValueTime, ValueAnchor, ValueTime2, ValueAnchor2, OffsetMinutes, ValueHours, ValueHours2,
     ActionType, ActionText, ActionValue, AllowEdit, UseEscalation, IsAutomatic, IsActive)
VALUES
    (@Name, @Shifts, @ApplyOn, @Days, @Semantic, @Field, @Cmp, @Kind,
     @Time, @Anchor, @Time2, @Anchor2, @Offset, @Hours, @Hours2,
     @ActionType, @ActionText, @ActionValue, @AllowEdit, @Escalation, @Auto, @Active);
""",
                command => AddParameters(command, rule));
        }
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM ShiftRules WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>سياق اليوم الفعّال لمعايير الاستحقاق: العطلة/الإجازة من الحالة، وإلا نوع اليوم.</summary>
    public static string EffectiveContext(DayAttendanceStore.DayRow day) => day.Status switch
    {
        "Holiday" => "Holiday",
        "Leave" => "Leave",
        _ => day.DayKind
    };

    /// <summary>هل تنطبق القاعدة على هذه اليومية (النطاق: مناوبة/معايير استحقاق/يوم أسبوع)؟</summary>
    public static bool AppliesTo(ShiftRule rule, DayAttendanceStore.DayRow day)
    {
        if (rule.ShiftTypeIdList is { Count: > 0 } shifts &&
            (day.ShiftTypeId == null || !shifts.Contains(day.ShiftTypeId.Value)))
            return false;

        if (rule.ApplyOn != "All" && EffectiveContext(day) != rule.ApplyOn)
            return false;

        if (rule.WeekDayList is { Count: > 0 } days &&
            !days.Contains(DayAttendanceStore.ToDayIndex(day.WorkDate)))
            return false;

        return true;
    }

    /// <summary>
    /// تقييم شرط القاعدة على يومية. المرجع النسبي (بدء/انتهاء المناوبة) يُؤخذ من
    /// تعريف يوم المناوبة، والمقارنة الزمنية بالدقائق-من-منتصف-الليل مع مرساة يوم
    /// (تدعم عبور منتصف الليل و«بين»). يعيد null إن لم يتحقق، أو نص ملخص التحقق.
    /// </summary>
    /// <summary>
    /// عدد أزواج البصمات لكل (موظف × يوم × دلالة) — يغذّي الشروط ذات النطاق
    /// الدلالي مثل «عدد المرات». يُبنى مرة للشهر بـ<see cref="SemanticCountsAsync"/>.
    /// </summary>
    public sealed class SemanticCounts
    {
        private readonly Dictionary<(int Employee, DateOnly Date, int Semantic), int> _map = new();

        public void Add(int employeeId, DateOnly date, int semanticId, int count) =>
            _map[(employeeId, date, semanticId)] = count;

        public int Count(int employeeId, DateOnly date, int semanticId) =>
            _map.TryGetValue((employeeId, date, semanticId), out var c) ? c : 0;
    }

    public static string? Evaluate(ShiftRule rule, DayAttendanceStore.DayRow day,
        ShiftTypeStore.ShiftDay? shiftDay, SemanticCounts? semanticCounts = null)
    {
        switch (rule.ConditionField)
        {
            case "PunchCount":
            {
                // شرط بنطاق دلالي: يلزمه نوع بصمة محدد على القاعدة
                if (!rule.PunchSemanticId.HasValue || rule.PunchSemanticId.Value <= 0) return null;
                if (semanticCounts == null) return null;

                var count = semanticCounts.Count(day.EmployeeId, day.WorkDate, rule.PunchSemanticId.Value);
                if (count == 0) return null;

                return CompareHours(rule, count)
                    ? $"عدد المرات {count} ({HoursCriterion(rule)})"
                    : null;
            }

            case "Absent":
                return day.Status == "Absent" ? "غياب بلا بصمات" : null;

            case "MissingCheckOut":
                return day.Status == "Incomplete" ? $"دخول {day.CheckIn:HH\\:mm} بلا ختم خروج" : null;

            case "Duration":
                return CompareHours(rule, day.WorkedHours)
                    ? $"مدة العمل {day.WorkedHours:0.##} س ({HoursCriterion(rule)})" : null;

            case "LateHours":
                return CompareHours(rule, day.LateHours)
                    ? $"تأخير {day.LateHours:0.##} س ({HoursCriterion(rule)})" : null;

            case "EarlyLeaveHours":
                return CompareHours(rule, day.EarlyLeaveHours)
                    ? $"خروج مبكر {day.EarlyLeaveHours:0.##} س ({HoursCriterion(rule)})" : null;

            case "CheckIn":
            case "CheckOut":
            {
                var punch = rule.ConditionField == "CheckIn" ? day.CheckIn : day.CheckOut;
                if (!punch.HasValue) return null;

                var punchMinutes = (punch.Value - day.WorkDate.ToDateTime(TimeOnly.MinValue)).TotalMinutes;
                var lower = ResolveReferenceMinutes(rule, shiftDay, rule.ValueTime, rule.ValueAnchor);
                if (lower == null) return null;

                bool matched;
                if (rule.Comparison == "Between")
                {
                    var upper = ResolveReferenceMinutes(rule, shiftDay, rule.ValueTime2, rule.ValueAnchor2);
                    if (upper == null) return null;
                    matched = punchMinutes >= lower && punchMinutes <= upper;
                }
                else
                {
                    matched = rule.Comparison switch
                    {
                        "Before" => punchMinutes < lower,
                        "BeforeOrAt" => punchMinutes <= lower,
                        "After" => punchMinutes > lower,
                        "AfterOrAt" => punchMinutes >= lower,
                        _ => false
                    };
                }

                return matched
                    ? $"{LabelOf(ConditionFields, rule.ConditionField)} {punch:HH\\:mm} {LabelOf(Comparisons, rule.Comparison)} {TimeCriterion(rule)}"
                    : null;
            }

            default:
                return null;
        }
    }

    private static bool CompareHours(ShiftRule rule, decimal actual) => rule.Comparison switch
    {
        "MoreThan" => actual > rule.ValueHours,
        "LessThan" => actual < rule.ValueHours,
        "Between" => actual >= rule.ValueHours && actual <= rule.ValueHours2,
        _ => false
    };

    private static string HoursCriterion(ShiftRule rule) => rule.Comparison == "Between"
        ? $"بين {rule.ValueHours:0.##} و{rule.ValueHours2:0.##}"
        : $"{LabelOf(Comparisons, rule.Comparison)} {rule.ValueHours:0.##}";

    private static string TimeCriterion(ShiftRule rule) => rule.Comparison == "Between"
        ? $"{FormatMinutes(ResolveReferenceMinutes(rule, null, rule.ValueTime, rule.ValueAnchor))} و{FormatMinutes(ResolveReferenceMinutes(rule, null, rule.ValueTime2, rule.ValueAnchor2))}"
        : rule.ValueTime ?? LabelOf(ValueKinds, rule.ValueKind);

    private static string FormatMinutes(double? minutes)
    {
        if (minutes == null) return "؟";
        var m = ((int)Math.Round(minutes.Value) % 1440 + 1440) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    private static int AnchorOffset(string anchor) => anchor switch
    {
        "Prev" => -1440,
        "Next" => 1440,
        _ => 0
    };

    /// <summary>حل المرجع الزمني إلى دقائق-من-منتصف-ليل-يوم-العمل (يشمل المرساة والإزاحة).</summary>
    private static double? ResolveReferenceMinutes(ShiftRule rule, ShiftTypeStore.ShiftDay? shiftDay,
        string? valueTime, string anchor)
    {
        var anchorOffset = AnchorOffset(anchor);
        switch (rule.ValueKind)
        {
            case "Time":
                return TimeSpan.TryParse(valueTime, out var absolute)
                    ? absolute.TotalMinutes + anchorOffset : null;
            case "ShiftStart":
                return TimeSpan.TryParse(shiftDay?.StartTime, out var start)
                    ? start.TotalMinutes + rule.OffsetMinutes + anchorOffset : null;
            case "ShiftEnd":
                return TimeSpan.TryParse(shiftDay?.EndTime, out var end)
                    ? end.TotalMinutes + rule.OffsetMinutes + anchorOffset : null;
            default:
                return null;
        }
    }

    private static void AddParameters(System.Data.Common.DbCommand command, ShiftRule rule)
    {
        HrmsDatabase.AddParameter(command, "@Name", rule.Name);
        HrmsDatabase.AddParameter(command, "@Shifts", rule.ShiftTypeIds);
        HrmsDatabase.AddParameter(command, "@ApplyOn", rule.ApplyOn);
        HrmsDatabase.AddParameter(command, "@Days", rule.WeekDays);
        HrmsDatabase.AddParameter(command, "@Semantic", (object?)rule.PunchSemanticId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Field", rule.ConditionField);
        HrmsDatabase.AddParameter(command, "@Cmp", rule.Comparison);
        HrmsDatabase.AddParameter(command, "@Kind", rule.ValueKind);
        HrmsDatabase.AddParameter(command, "@Time", (object?)rule.ValueTime ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Anchor", rule.ValueAnchor);
        HrmsDatabase.AddParameter(command, "@Time2", (object?)rule.ValueTime2 ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Anchor2", rule.ValueAnchor2);
        HrmsDatabase.AddParameter(command, "@Offset", rule.OffsetMinutes);
        HrmsDatabase.AddParameter(command, "@Hours", rule.ValueHours);
        HrmsDatabase.AddParameter(command, "@Hours2", rule.ValueHours2);
        HrmsDatabase.AddParameter(command, "@ActionType", rule.ActionType);
        HrmsDatabase.AddParameter(command, "@ActionText", rule.ActionText);
        HrmsDatabase.AddParameter(command, "@ActionValue", rule.ActionValue);
        HrmsDatabase.AddParameter(command, "@AllowEdit", rule.AllowEdit ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Escalation", rule.UseEscalation ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Auto", rule.IsAutomatic ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Active", rule.IsActive ? 1 : 0);
    }
}
