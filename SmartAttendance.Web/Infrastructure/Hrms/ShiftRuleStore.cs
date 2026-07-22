using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قواعد المناوبات (نمط كيان — قسم 10 بدراسة الحضور): القاعدة = نطاق (مناوبات ×
/// سياق يوم × أيام أسبوع) + شرط (حقل يومية × مقارنة × قيمة زمنية مطلقة/نسبية
/// لبداية-نهاية المناوبة أو عدد ساعات) + أثر (مخالفة/ملاحظة، نص الإجراء، تلقائي؟).
/// التقييم يمر على يوميات DayAttendances ويولّد اقتراحات AttendanceRecommendations
/// (راجع RecommendationStore). أنواع أثر كيان الأخرى (إجازة/أوفرتايم/دخل/اقتطاع)
/// تُضاف عند بناء الرواتب.
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
        ("MissingCheckOut", "ختم الخروج مفقود")
    };

    public static readonly (string Key, string Label)[] Comparisons =
    {
        ("Before", "قبل"),
        ("BeforeOrAt", "قبل/في"),
        ("After", "بعد"),
        ("AfterOrAt", "بعد/في"),
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

    public static readonly (string Key, string Label)[] ApplyOnOptions =
    {
        ("All", "كل الأيام"),
        ("Work", "أيام العمل"),
        ("Weekend", "أيام العطلة"),
        ("Rest", "أيام الراحة")
    };

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    public sealed class ShiftRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShiftTypeIds { get; set; } = string.Empty;   // CSV، فارغ = كل المناوبات
        public string ApplyOn { get; set; } = "Work";              // All | Work | Weekend | Rest
        public string WeekDays { get; set; } = string.Empty;       // CSV مؤشرات 0..6، فارغ = الكل
        public string ConditionField { get; set; } = "CheckIn";
        public string Comparison { get; set; } = "After";
        public string ValueKind { get; set; } = "Time";
        public string? ValueTime { get; set; }                     // HH:mm للمطلق
        public int OffsetMinutes { get; set; }                     // للنسبي: + بعد المرجع، - قبله
        public decimal ValueHours { get; set; }
        public string ActionType { get; set; } = "Violation";      // Violation | Note
        public string ActionText { get; set; } = string.Empty;
        public bool IsAutomatic { get; set; }
        public bool IsActive { get; set; } = true;

        public List<int> ShiftTypeIdList =>
            ShiftTypeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();

        public List<int> WeekDayList =>
            WeekDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v is >= 0 and <= 6).ToList();

        /// <summary>جملة القاعدة بأسلوب كيان: «في حالة [حقل] [شرط] [قيمة] ← [إجراء]».</summary>
        public string Sentence
        {
            get
            {
                var fieldLabel = LabelOf(ConditionFields, ConditionField);
                if (ConditionField is "Absent" or "MissingCheckOut")
                    return $"في حالة {fieldLabel} ← {ActionText}";

                var value = ValueKind switch
                {
                    "Hours" => $"{ValueHours:0.##} ساعة",
                    "Time" => ValueTime ?? "؟",
                    _ => LabelOf(ValueKinds, ValueKind) + OffsetMinutes switch
                    {
                        > 0 => $" +{OffsetMinutes}د",
                        < 0 => $" -{-OffsetMinutes}د",
                        _ => ""
                    }
                };
                return $"في حالة {fieldLabel} {LabelOf(Comparisons, Comparison)} {value} ← {ActionText}";
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
        ConditionField nvarchar(30) NOT NULL,
        Comparison nvarchar(20) NOT NULL DEFAULT(N'After'),
        ValueKind nvarchar(20) NOT NULL DEFAULT(N'Time'),
        ValueTime nvarchar(5) NULL,
        OffsetMinutes int NOT NULL DEFAULT(0),
        ValueHours decimal(5,2) NOT NULL DEFAULT(0),
        ActionType nvarchar(20) NOT NULL DEFAULT(N'Violation'),
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        IsAutomatic bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
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
                ConditionField = HrmsDatabase.GetString(reader, "ConditionField"),
                Comparison = HrmsDatabase.GetString(reader, "Comparison"),
                ValueKind = HrmsDatabase.GetString(reader, "ValueKind"),
                ValueTime = HrmsDatabase.GetString(reader, "ValueTime") is { Length: > 0 } t ? t : null,
                OffsetMinutes = HrmsDatabase.GetInt(reader, "OffsetMinutes"),
                ValueHours = reader["ValueHours"] is decimal h ? h : 0,
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
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
    ConditionField = @Field, Comparison = @Cmp, ValueKind = @Kind, ValueTime = @Time,
    OffsetMinutes = @Offset, ValueHours = @Hours, ActionType = @ActionType,
    ActionText = @ActionText, IsAutomatic = @Auto, IsActive = @Active
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
    (Name, ShiftTypeIds, ApplyOn, WeekDays, ConditionField, Comparison, ValueKind,
     ValueTime, OffsetMinutes, ValueHours, ActionType, ActionText, IsAutomatic, IsActive)
VALUES
    (@Name, @Shifts, @ApplyOn, @Days, @Field, @Cmp, @Kind,
     @Time, @Offset, @Hours, @ActionType, @ActionText, @Auto, @Active);
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

    /// <summary>هل تنطبق القاعدة على هذه اليومية (النطاق: مناوبة/سياق يوم/يوم أسبوع)؟</summary>
    public static bool AppliesTo(ShiftRule rule, DayAttendanceStore.DayRow day)
    {
        if (rule.ShiftTypeIdList is { Count: > 0 } shifts &&
            (day.ShiftTypeId == null || !shifts.Contains(day.ShiftTypeId.Value)))
            return false;

        if (rule.ApplyOn != "All" && day.DayKind != rule.ApplyOn)
            return false;

        if (rule.WeekDayList is { Count: > 0 } days &&
            !days.Contains(DayAttendanceStore.ToDayIndex(day.WorkDate)))
            return false;

        return true;
    }

    /// <summary>
    /// تقييم شرط القاعدة على يومية. المرجع النسبي (بدء/انتهاء المناوبة) يُؤخذ من
    /// تعريف يوم المناوبة نفسه. يعيد null إن لم يتحقق، أو نص ملخص التحقق.
    /// </summary>
    public static string? Evaluate(ShiftRule rule, DayAttendanceStore.DayRow day,
        ShiftTypeStore.ShiftDay? shiftDay)
    {
        switch (rule.ConditionField)
        {
            case "Absent":
                return day.Status == "Absent" ? "غياب بلا بصمات" : null;

            case "MissingCheckOut":
                return day.Status == "Incomplete" ? $"دخول {day.CheckIn:HH\\:mm} بلا ختم خروج" : null;

            case "Duration":
                return CompareHours(rule, day.WorkedHours)
                    ? $"مدة العمل {day.WorkedHours:0.##} س ({LabelOf(Comparisons, rule.Comparison)} {rule.ValueHours:0.##})" : null;

            case "LateHours":
                return CompareHours(rule, day.LateHours)
                    ? $"تأخير {day.LateHours:0.##} س ({LabelOf(Comparisons, rule.Comparison)} {rule.ValueHours:0.##})" : null;

            case "EarlyLeaveHours":
                return CompareHours(rule, day.EarlyLeaveHours)
                    ? $"خروج مبكر {day.EarlyLeaveHours:0.##} س ({LabelOf(Comparisons, rule.Comparison)} {rule.ValueHours:0.##})" : null;

            case "CheckIn":
            case "CheckOut":
            {
                var punch = rule.ConditionField == "CheckIn" ? day.CheckIn : day.CheckOut;
                if (!punch.HasValue) return null;
                var reference = ResolveReference(rule, shiftDay);
                if (reference == null) return null;
                var matched = rule.Comparison switch
                {
                    "Before" => punch.Value.TimeOfDay < reference,
                    "BeforeOrAt" => punch.Value.TimeOfDay <= reference,
                    "After" => punch.Value.TimeOfDay > reference,
                    "AfterOrAt" => punch.Value.TimeOfDay >= reference,
                    _ => false
                };
                return matched
                    ? $"{LabelOf(ConditionFields, rule.ConditionField)} {punch:HH\\:mm} {LabelOf(Comparisons, rule.Comparison)} {reference:hh\\:mm}"
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
        _ => false
    };

    private static TimeSpan? ResolveReference(ShiftRule rule, ShiftTypeStore.ShiftDay? shiftDay)
    {
        switch (rule.ValueKind)
        {
            case "Time":
                return TimeSpan.TryParse(rule.ValueTime, out var absolute) ? absolute : null;
            case "ShiftStart":
                return TimeSpan.TryParse(shiftDay?.StartTime, out var start)
                    ? start + TimeSpan.FromMinutes(rule.OffsetMinutes) : null;
            case "ShiftEnd":
                return TimeSpan.TryParse(shiftDay?.EndTime, out var end)
                    ? end + TimeSpan.FromMinutes(rule.OffsetMinutes) : null;
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
        HrmsDatabase.AddParameter(command, "@Field", rule.ConditionField);
        HrmsDatabase.AddParameter(command, "@Cmp", rule.Comparison);
        HrmsDatabase.AddParameter(command, "@Kind", rule.ValueKind);
        HrmsDatabase.AddParameter(command, "@Time", (object?)rule.ValueTime ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Offset", rule.OffsetMinutes);
        HrmsDatabase.AddParameter(command, "@Hours", rule.ValueHours);
        HrmsDatabase.AddParameter(command, "@ActionType", rule.ActionType);
        HrmsDatabase.AddParameter(command, "@ActionText", rule.ActionText);
        HrmsDatabase.AddParameter(command, "@Auto", rule.IsAutomatic ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Active", rule.IsActive ? 1 : 0);
    }
}
