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

    public sealed class ShiftType
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string ColorHex { get; set; } = "#12D9E3";
        public bool IsFlexible { get; set; }
        public decimal FlexDailyHours { get; set; }       // للمرنة: ساعات مطلوبة يومياً
        public bool IsActive { get; set; } = true;
        public List<ShiftDay> Days { get; set; } = new();

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

        var byShift = days.GroupBy(d => d.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Day).ToList());

        foreach (var shift in shifts)
        {
            shift.Days = byShift.TryGetValue(shift.Id, out var list) ? list : new();
        }
        return shifts;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, ShiftType shift)
    {
        await EnsureAsync(dbContext);

        int id;
        if (shift.Id > 0)
        {
            id = shift.Id;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE ShiftTypes
SET Name = @Name, NameEn = @NameEn, ColorHex = @Color,
    IsFlexible = @Flex, FlexDailyHours = @FlexHours, IsActive = @Active
WHERE Id = @Id;
DELETE FROM ShiftTypeDays WHERE ShiftTypeId = @Id;
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
INSERT INTO ShiftTypes (Name, NameEn, ColorHex, IsFlexible, FlexDailyHours, IsActive)
VALUES (@Name, @NameEn, @Color, @Flex, @FlexHours, @Active);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command => AddShiftParameters(command, shift));
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
        HrmsDatabase.AddParameter(command, "@Active", shift.IsActive ? 1 : 0);
    }
}
