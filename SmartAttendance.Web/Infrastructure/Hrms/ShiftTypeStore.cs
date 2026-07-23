using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// أنواع المناوبات (نمط كيان — قسم 11 بدراسة الحضور): المناوبة تُعرَّف يوماً-بيوم
/// بمصفوفة 7 أيام (السبت..الجمعة)، كل يوم له نوع (عمل/عطلة أسبوعية/راحة) ووقت
/// دخول/خروج وساعات عمل. المناوبة إما «ثابتة» (أوقات محددة لكل يوم) أو «مرنة»
/// (عدد ساعات مطلوب يومياً بلا أوقات). لكل مناوبة لون يميزها بمستعرض الحضور.
/// هذا الكيان الجديد يوازي جدول Shifts القديم البسيط ولا يمسه — الترحيل لاحقاً.
/// ملاحظة: «الفترات المتعددة» (سبليت شفت) مؤجلة للمرحلة التالية.
/// </summary>
public static class ShiftTypeStore
{
    /// <summary>أيام الأسبوع بترتيب المنطقة (0=السبت .. 6=الجمعة) — نفس ترتيب كيان.</summary>
    public static readonly string[] DayNames =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    /// <summary>ألوان المناوبات الجاهزة (مطابقة لفكرة كيان: 8 ألوان مميزة للمستعرض).</summary>
    public static readonly string[] Colors =
        { "#12D9E3", "#4ade80", "#facc15", "#f97316", "#f87171", "#a78bfa", "#60a5fa", "#f472b6" };

    public sealed class ShiftDay
    {
        public int DayIndex { get; set; }                 // 0=السبت .. 6=الجمعة
        public string DayKind { get; set; } = "Work";     // Work | Weekend | Rest
        public string? StartTime { get; set; }            // "HH:mm" — للثابتة بيوم عمل
        public string? EndTime { get; set; }
        public decimal WorkHours { get; set; }            // ساعات اليوم (تُشتق أو تُدخل)

        public string DayName => DayNames[Math.Clamp(DayIndex, 0, 6)];
        public bool IsWork => DayKind == "Work";
    }

    /// <summary>فترة عمل بمناوبة «بفترات متعددة» (سبليت شفت) — بداية/نهاية مشتركة لكل أيام العمل.</summary>
    public sealed class ShiftPeriod
    {
        public int Ordinal { get; set; }
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "13:00";
        public decimal Hours => ComputeHours(StartTime, EndTime);
    }

    public sealed class ShiftType
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string ColorHex { get; set; } = "#12D9E3";
        public bool IsFlexible { get; set; }
        public decimal FlexDailyHours { get; set; }       // للمرنة: ساعات مطلوبة يومياً
        public bool MultiPeriod { get; set; }             // مناوبة بفترات متعددة (سبليت شفت)

        // ===== قواعد سلوك المناوبة (أسفل اختيار الأيام بكيان) =====
        public bool FillMissingCheckIn { get; set; }          // تعبئة وقت بدء المناوبة لبصمة دخول مفقودة
        public bool FillMissingCheckOut { get; set; }         // تعبئة وقت انتهاء المناوبة لبصمة خروج مفقودة
        public bool StripSemantics { get; set; }              // تجريد فترة الدلالات (استراحة/صلاة…) من الحضور
        public bool ConsiderPermissionsOutsideShift { get; set; } // احتساب ساعات المغادرة خارج المناوبة
        public bool ExcludePermsOutsideStartFromLate { get; set; } // استثناء المغادرات خارج بداية المناوبة من التأخير
        public string TotalDurationMode { get; set; } = "WorkOnly"; // WorkOnly | IncludeOff | Both
        public bool AvailableInRoster { get; set; } = true;   // متاحة بجدول الحضور (الروستر)
        public bool RequestableFromEss { get; set; }          // قابلة للطلب من الخدمة الذاتية

        // ===== السماحيات والحدود (فترة السماح + نافذة البصم + منتصف المناوبة) =====
        public int LatenessGraceMinutes { get; set; }         // فترة السماح للتأخير (دقائق)
        public int EarlyLeaveGraceMinutes { get; set; }       // فترة السماح للخروج المبكر (دقائق)
        public string? TimeLimitFrom { get; set; }            // حد وقت بدء المناوبة HH:mm (أبكر بصمة صالحة)
        public bool TimeLimitFromDayBefore { get; set; }      // مرساة الحد: من اليوم السابق
        public string? TimeLimitTo { get; set; }              // حد وقت انتهاء المناوبة HH:mm (أحدث بصمة صالحة)
        public bool TimeLimitToDayAfter { get; set; }         // مرساة الحد: إلى اليوم التالي
        public string? MidShiftTime { get; set; }             // وقت منتصف المناوبة HH:mm (تقسيم بصمات المناوبة العابرة لمنتصف الليل)

        public bool IsActive { get; set; } = true;
        public List<ShiftDay> Days { get; set; } = new();
        public List<ShiftPeriod> Periods { get; set; } = new();

        /// <summary>أسماء أيام العطلة/الراحة — لعمود القائمة (مثل عرض كيان).</summary>
        public string OffDaysText => string.Join(" ",
            Days.Where(d => !d.IsWork).Select(d => d.DayName)) is { Length: > 0 } text ? text : "—";
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ShiftTypes', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        ColorHex nvarchar(9) NOT NULL DEFAULT(N'#12D9E3'),
        IsFlexible bit NOT NULL DEFAULT(0),
        FlexDailyHours decimal(5,2) NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('ShiftTypeDays', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypeDays
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ShiftTypeId int NOT NULL,
        DayIndex int NOT NULL,                -- 0=السبت .. 6=الجمعة
        DayKind nvarchar(20) NOT NULL DEFAULT(N'Work'),
        StartTime nvarchar(5) NULL,           -- HH:mm
        EndTime nvarchar(5) NULL,
        WorkHours decimal(5,2) NOT NULL DEFAULT(0)
    );
    CREATE UNIQUE INDEX UX_ShiftTypeDays_Shift_Day ON ShiftTypeDays (ShiftTypeId, DayIndex);
END;

-- الفترات المتعددة (سبليت شفت) — idempotent
IF COL_LENGTH('ShiftTypes','MultiPeriod') IS NULL ALTER TABLE ShiftTypes ADD MultiPeriod bit NOT NULL CONSTRAINT DF_ShiftTypes_MultiPeriod DEFAULT(0);
IF OBJECT_ID('ShiftTypePeriods', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypePeriods
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ShiftTypeId int NOT NULL,
        Ordinal int NOT NULL,
        StartTime nvarchar(5) NOT NULL,       -- HH:mm
        EndTime nvarchar(5) NOT NULL
    );
    CREATE INDEX IX_ShiftTypePeriods_Shift ON ShiftTypePeriods (ShiftTypeId, Ordinal);
END;

-- قواعد سلوك المناوبة (idempotent)
IF COL_LENGTH('ShiftTypes','FillMissingCheckIn') IS NULL ALTER TABLE ShiftTypes ADD FillMissingCheckIn bit NOT NULL CONSTRAINT DF_ST_FMI DEFAULT(0);
IF COL_LENGTH('ShiftTypes','FillMissingCheckOut') IS NULL ALTER TABLE ShiftTypes ADD FillMissingCheckOut bit NOT NULL CONSTRAINT DF_ST_FMO DEFAULT(0);
IF COL_LENGTH('ShiftTypes','StripSemantics') IS NULL ALTER TABLE ShiftTypes ADD StripSemantics bit NOT NULL CONSTRAINT DF_ST_STS DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ConsiderPermissionsOutsideShift') IS NULL ALTER TABLE ShiftTypes ADD ConsiderPermissionsOutsideShift bit NOT NULL CONSTRAINT DF_ST_CPO DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ExcludePermsOutsideStartFromLate') IS NULL ALTER TABLE ShiftTypes ADD ExcludePermsOutsideStartFromLate bit NOT NULL CONSTRAINT DF_ST_EPL DEFAULT(0);
IF COL_LENGTH('ShiftTypes','TotalDurationMode') IS NULL ALTER TABLE ShiftTypes ADD TotalDurationMode nvarchar(20) NOT NULL CONSTRAINT DF_ST_TDM DEFAULT(N'WorkOnly');
IF COL_LENGTH('ShiftTypes','AvailableInRoster') IS NULL ALTER TABLE ShiftTypes ADD AvailableInRoster bit NOT NULL CONSTRAINT DF_ST_AIR DEFAULT(1);
IF COL_LENGTH('ShiftTypes','RequestableFromEss') IS NULL ALTER TABLE ShiftTypes ADD RequestableFromEss bit NOT NULL CONSTRAINT DF_ST_RFE DEFAULT(0);
IF COL_LENGTH('ShiftTypes','LatenessGraceMinutes') IS NULL ALTER TABLE ShiftTypes ADD LatenessGraceMinutes int NOT NULL CONSTRAINT DF_ST_LGM DEFAULT(0);
IF COL_LENGTH('ShiftTypes','EarlyLeaveGraceMinutes') IS NULL ALTER TABLE ShiftTypes ADD EarlyLeaveGraceMinutes int NOT NULL CONSTRAINT DF_ST_ELG DEFAULT(0);
IF COL_LENGTH('ShiftTypes','TimeLimitFrom') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitFrom nvarchar(5) NULL;
IF COL_LENGTH('ShiftTypes','TimeLimitFromDayBefore') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitFromDayBefore bit NOT NULL CONSTRAINT DF_ST_TLFB DEFAULT(0);
IF COL_LENGTH('ShiftTypes','TimeLimitTo') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitTo nvarchar(5) NULL;
IF COL_LENGTH('ShiftTypes','TimeLimitToDayAfter') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitToDayAfter bit NOT NULL CONSTRAINT DF_ST_TLTA DEFAULT(0);
IF COL_LENGTH('ShiftTypes','MidShiftTime') IS NULL ALTER TABLE ShiftTypes ADD MidShiftTime nvarchar(5) NULL;
""");
    }

    public static async Task<List<ShiftType>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);

        var shifts = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypes ORDER BY Name;",
            command => { },
            ReadShift);

        var days = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypeDays ORDER BY ShiftTypeId, DayIndex;",
            command => { },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                Day = ReadDay(reader)
            });

        var periods = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypePeriods ORDER BY ShiftTypeId, Ordinal;",
            command => { },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                Period = new ShiftPeriod
                {
                    Ordinal = HrmsDatabase.GetInt(reader, "Ordinal"),
                    StartTime = HrmsDatabase.GetString(reader, "StartTime") is { Length: > 0 } s ? s : "09:00",
                    EndTime = HrmsDatabase.GetString(reader, "EndTime") is { Length: > 0 } e ? e : "13:00"
                }
            });

        var daysByShift = days.GroupBy(d => d.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Day).ToList());
        var periodsByShift = periods.GroupBy(p => p.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Period).ToList());

        foreach (var shift in shifts)
        {
            shift.Days = daysByShift.TryGetValue(shift.Id, out var list) ? list : new();
            shift.Periods = periodsByShift.TryGetValue(shift.Id, out var pl) ? pl : new();
        }
        return shifts;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, ShiftType shift)
    {
        await EnsureAsync(dbContext);

        // مناوبة بفترات متعددة: ساعات كل يوم عمل = مجموع ساعات الفترات المشتركة،
        // ووقتا اليوم = بداية أول فترة/نهاية آخر فترة (للعرض ومرساة المحرك).
        if (shift.MultiPeriod && shift.Periods.Count > 0)
        {
            var ordered = shift.Periods.OrderBy(p => p.Ordinal).ToList();
            var total = ordered.Sum(p => p.Hours);
            var firstStart = ordered.First().StartTime;
            var lastEnd = ordered.Last().EndTime;
            foreach (var day in shift.Days.Where(d => d.IsWork))
            {
                day.StartTime = firstStart;
                day.EndTime = lastEnd;
                day.WorkHours = total;
            }
        }

        int id;
        if (shift.Id > 0)
        {
            id = shift.Id;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE ShiftTypes
SET Name = @Name, NameEn = @NameEn, ColorHex = @Color,
    IsFlexible = @Flex, FlexDailyHours = @FlexHours, MultiPeriod = @Multi,
    FillMissingCheckIn = @FMI, FillMissingCheckOut = @FMO, StripSemantics = @STS,
    ConsiderPermissionsOutsideShift = @CPO, ExcludePermsOutsideStartFromLate = @EPL,
    TotalDurationMode = @TDM, AvailableInRoster = @AIR, RequestableFromEss = @RFE,
    LatenessGraceMinutes = @LGM, EarlyLeaveGraceMinutes = @ELG,
    TimeLimitFrom = @TLF, TimeLimitFromDayBefore = @TLFB, TimeLimitTo = @TLT, TimeLimitToDayAfter = @TLTA, MidShiftTime = @MST,
    IsActive = @Active
WHERE Id = @Id;
DELETE FROM ShiftTypeDays WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypePeriods WHERE ShiftTypeId = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", shift.Id);
                    AddShiftParameters(command, shift);
                });
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO ShiftTypes (Name, NameEn, ColorHex, IsFlexible, FlexDailyHours, MultiPeriod,
    FillMissingCheckIn, FillMissingCheckOut, StripSemantics, ConsiderPermissionsOutsideShift,
    ExcludePermsOutsideStartFromLate, TotalDurationMode, AvailableInRoster, RequestableFromEss,
    LatenessGraceMinutes, EarlyLeaveGraceMinutes, TimeLimitFrom, TimeLimitFromDayBefore, TimeLimitTo, TimeLimitToDayAfter, MidShiftTime, IsActive)
VALUES (@Name, @NameEn, @Color, @Flex, @FlexHours, @Multi,
    @FMI, @FMO, @STS, @CPO, @EPL, @TDM, @AIR, @RFE,
    @LGM, @ELG, @TLF, @TLFB, @TLT, @TLTA, @MST, @Active);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command => AddShiftParameters(command, shift));
        }

        if (shift.MultiPeriod)
        {
            var ord = 0;
            foreach (var period in shift.Periods.OrderBy(p => p.Ordinal))
            {
                var current = period;
                var thisOrd = ord++;
                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "INSERT INTO ShiftTypePeriods (ShiftTypeId, Ordinal, StartTime, EndTime) VALUES (@S, @O, @St, @En);",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@S", id);
                        HrmsDatabase.AddParameter(command, "@O", thisOrd);
                        HrmsDatabase.AddParameter(command, "@St", current.StartTime);
                        HrmsDatabase.AddParameter(command, "@En", current.EndTime);
                    });
            }
        }

        foreach (var day in shift.Days.OrderBy(d => d.DayIndex))
        {
            var current = day;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ShiftTypeDays (ShiftTypeId, DayIndex, DayKind, StartTime, EndTime, WorkHours)
VALUES (@ShiftTypeId, @DayIndex, @DayKind, @StartTime, @EndTime, @WorkHours);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@ShiftTypeId", id);
                    HrmsDatabase.AddParameter(command, "@DayIndex", current.DayIndex);
                    HrmsDatabase.AddParameter(command, "@DayKind", current.DayKind);
                    HrmsDatabase.AddParameter(command, "@StartTime", (object?)current.StartTime ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@EndTime", (object?)current.EndTime ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@WorkHours", current.WorkHours);
                });
        }

        return id;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE FROM ShiftTypeDays WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypePeriods WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypes WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>يحسب ساعات اليوم من وقتي الدخول/الخروج (يدعم عبور منتصف الليل).</summary>
    public static decimal ComputeHours(string? start, string? end)
    {
        if (!TimeSpan.TryParse(start, out var s) || !TimeSpan.TryParse(end, out var e)) return 0;
        var span = e - s;
        if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1); // مناوبة ليلية تعبر منتصف الليل
        return Math.Round((decimal)span.TotalHours, 2);
    }

    private static ShiftType ReadShift(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Name = HrmsDatabase.GetString(reader, "Name"),
        NameEn = HrmsDatabase.GetString(reader, "NameEn"),
        ColorHex = HrmsDatabase.GetString(reader, "ColorHex") is { Length: > 0 } c ? c : "#12D9E3",
        IsFlexible = HrmsDatabase.GetBool(reader, "IsFlexible"),
        FlexDailyHours = reader["FlexDailyHours"] is decimal f ? f : 0,
        MultiPeriod = HrmsDatabase.GetBool(reader, "MultiPeriod"),
        FillMissingCheckIn = HrmsDatabase.GetBool(reader, "FillMissingCheckIn"),
        FillMissingCheckOut = HrmsDatabase.GetBool(reader, "FillMissingCheckOut"),
        StripSemantics = HrmsDatabase.GetBool(reader, "StripSemantics"),
        ConsiderPermissionsOutsideShift = HrmsDatabase.GetBool(reader, "ConsiderPermissionsOutsideShift"),
        ExcludePermsOutsideStartFromLate = HrmsDatabase.GetBool(reader, "ExcludePermsOutsideStartFromLate"),
        TotalDurationMode = HrmsDatabase.GetString(reader, "TotalDurationMode") is { Length: > 0 } td ? td : "WorkOnly",
        AvailableInRoster = HrmsDatabase.GetBool(reader, "AvailableInRoster"),
        RequestableFromEss = HrmsDatabase.GetBool(reader, "RequestableFromEss"),
        LatenessGraceMinutes = HrmsDatabase.GetInt(reader, "LatenessGraceMinutes"),
        EarlyLeaveGraceMinutes = HrmsDatabase.GetInt(reader, "EarlyLeaveGraceMinutes"),
        TimeLimitFrom = HrmsDatabase.GetString(reader, "TimeLimitFrom") is { Length: > 0 } tlf ? tlf : null,
        TimeLimitFromDayBefore = HrmsDatabase.GetBool(reader, "TimeLimitFromDayBefore"),
        TimeLimitTo = HrmsDatabase.GetString(reader, "TimeLimitTo") is { Length: > 0 } tlt ? tlt : null,
        TimeLimitToDayAfter = HrmsDatabase.GetBool(reader, "TimeLimitToDayAfter"),
        MidShiftTime = HrmsDatabase.GetString(reader, "MidShiftTime") is { Length: > 0 } mst ? mst : null,
        IsActive = HrmsDatabase.GetBool(reader, "IsActive")
    };

    private static ShiftDay ReadDay(System.Data.Common.DbDataReader reader) => new()
    {
        DayIndex = HrmsDatabase.GetInt(reader, "DayIndex"),
        DayKind = HrmsDatabase.GetString(reader, "DayKind") is { Length: > 0 } k ? k : "Work",
        StartTime = HrmsDatabase.GetString(reader, "StartTime") is { Length: > 0 } s ? s : null,
        EndTime = HrmsDatabase.GetString(reader, "EndTime") is { Length: > 0 } e ? e : null,
        WorkHours = reader["WorkHours"] is decimal w ? w : 0
    };

    private static void AddShiftParameters(System.Data.Common.DbCommand command, ShiftType shift)
    {
        HrmsDatabase.AddParameter(command, "@Name", shift.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)shift.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Color", shift.ColorHex);
        HrmsDatabase.AddParameter(command, "@Flex", shift.IsFlexible ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FlexHours", shift.FlexDailyHours);
        HrmsDatabase.AddParameter(command, "@Multi", shift.MultiPeriod ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FMI", shift.FillMissingCheckIn ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FMO", shift.FillMissingCheckOut ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@STS", shift.StripSemantics ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CPO", shift.ConsiderPermissionsOutsideShift ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@EPL", shift.ExcludePermsOutsideStartFromLate ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@TDM", string.IsNullOrWhiteSpace(shift.TotalDurationMode) ? "WorkOnly" : shift.TotalDurationMode);
        HrmsDatabase.AddParameter(command, "@AIR", shift.AvailableInRoster ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@RFE", shift.RequestableFromEss ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@LGM", shift.LatenessGraceMinutes);
        HrmsDatabase.AddParameter(command, "@ELG", shift.EarlyLeaveGraceMinutes);
        HrmsDatabase.AddParameter(command, "@TLF", (object?)shift.TimeLimitFrom ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@TLFB", shift.TimeLimitFromDayBefore ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@TLT", (object?)shift.TimeLimitTo ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@TLTA", shift.TimeLimitToDayAfter ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@MST", (object?)shift.MidShiftTime ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Active", shift.IsActive ? 1 : 0);
    }
}
