using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// دلالات البصمات (نمط كيان — قسم 3.2 بدراسة الحضور): تصنيف ثنائي اللغة لأنواع
/// البصمات (حضور/انصراف نظاميان + مخصصة مثل صلاة/استراحة/مهمة عمل). تغذّي لاحقاً:
/// أزرار البصم بالخدمة الذاتية، منشئ القواعد (نوع البصمة)، وطلبات البصمة المفقودة.
/// الدلالتان النظاميتان تُبذران تلقائياً ولا تُحذفان.
/// </summary>
public static class PunchSemanticStore
{
    public sealed class PunchSemantic
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        // ═══ الدلالة ككيان زمنيّ (نظير كيان) ═══
        //
        // `IsDeducted`: هل تُخصم فترة هذه الدلالة من مدّة الحضور؟ كانت الراية واحدة
        // على المناوبة (`ShiftTypes.StripSemantics`) للجميع — إمّا الكلّ أو لا أحد.
        // الآن: الراية تبقى المفتاح الرئيس (مطفأةً لا يُخصم شيء)، وهذا الحقل يقرّر
        // **أيّ** الدلالات تُخصم حين تكون مُشغَّلة. الافتراضي `true` = سلوك اليوم.
        //
        // `WindowFrom`/`WindowTo`: نافذة الدلالة المسموحة ("HH:mm"). فترةٌ تتجاوزها
        // تُقصّ عليها قبل الخصم — فاستراحةُ ساعتين بنافذة نصف ساعة تُخصم نصف ساعة
        // فقط ويبقى الباقي محسوباً على الموظف. null = بلا نافذة ⟹ بلا قصّ.
        public bool IsDeducted { get; set; } = true;
        public string? WindowFrom { get; set; }
        public string? WindowTo { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PunchSemantics', 'U') IS NULL
BEGIN
    CREATE TABLE PunchSemantics
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        IsSystem bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        SortOrder int NOT NULL DEFAULT(0),
        IsDeducted bit NOT NULL DEFAULT(1),
        WindowFrom time(0) NULL,
        WindowTo time(0) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM PunchSemantics WHERE IsSystem = 1)
BEGIN
    INSERT INTO PunchSemantics (Name, NameEn, IsSystem, IsActive, SortOrder)
    VALUES (N'حضور', N'Check-In', 1, 1, 0),
           (N'انصراف', N'Check-Out', 1, 1, 1);
END;
""");
    }

    public static async Task<List<PunchSemantic>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM PunchSemantics ORDER BY SortOrder, Name;",
            command => { },
            reader => new PunchSemantic
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                NameEn = HrmsDatabase.GetString(reader, "NameEn") is { Length: > 0 } en ? en : null,
                IsSystem = HrmsDatabase.GetBool(reader, "IsSystem"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder"),
                IsDeducted = HrmsDatabase.GetBool(reader, "IsDeducted"),
                WindowFrom = ReadTime(reader, "WindowFrom"),
                WindowTo = ReadTime(reader, "WindowTo")
            });
    }

    /// <summary>
    /// مُعرّف دلالة «حضور» النظامية — الزوج الذي يُحتسب في اشتقاق اليومية.
    /// كل ما عداه (استراحة/صلاة/مهمة عمل...) بصمات أخرى تُستثنى من أول-دخول
    /// وآخر-خروج. تُبذر الدلالة النظامية تلقائياً فلا تعود 0 عملياً.
    /// </summary>
    public static async Task<int> AttendanceSemanticIdAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT TOP 1 Id FROM PunchSemantics WHERE IsSystem = 1 ORDER BY SortOrder, Id;",
            command => { });
    }

    /// <summary>
    /// الدلالات غير-الحضورية النشطة — تغذّي «البصمات الأخرى».
    /// تُستثنى الدلالتان النظاميتان: صفّنا زوج (دخول/خروج) فدلالة «حضور» تغطّيه،
    /// و«انصراف» النظامية تخصّ أزرار البصم بالخدمة الذاتية لا تصنيف الأزواج.
    /// </summary>
    public static async Task<List<PunchSemantic>> OtherSemanticsAsync(ApplicationDbContext dbContext)
    {
        var all = await ListAsync(dbContext);

        return all.Where(s => s.IsActive && !s.IsSystem).ToList();
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, PunchSemantic semantic)
    {
        await EnsureAsync(dbContext);

        if (semantic.Id > 0)
        {
            // IsSystem لا يُعدّل — الاسم النظامي قابل للتحرير لكن يبقى محمياً من الحذف.
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE PunchSemantics
SET Name = @Name, NameEn = @NameEn, IsActive = @Active, SortOrder = @Sort,
    IsDeducted = @Deducted, WindowFrom = @WinFrom, WindowTo = @WinTo
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", semantic.Id);
                    AddParameters(command, semantic);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO PunchSemantics (Name, NameEn, IsSystem, IsActive, SortOrder, IsDeducted, WindowFrom, WindowTo)
VALUES (@Name, @NameEn, 0, @Active, @Sort, @Deducted, @WinFrom, @WinTo);
""",
                command => AddParameters(command, semantic));
        }
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PunchSemantics WHERE Id = @Id AND IsSystem = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static void AddParameters(System.Data.Common.DbCommand command, PunchSemantic semantic)
    {
        HrmsDatabase.AddParameter(command, "@Name", semantic.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)semantic.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Active", semantic.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Sort", semantic.SortOrder);
        HrmsDatabase.AddParameter(command, "@Deducted", semantic.IsDeducted ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@WinFrom", ToTime(semantic.WindowFrom));
        HrmsDatabase.AddParameter(command, "@WinTo", ToTime(semantic.WindowTo));
    }

    /// <summary>عمود time ⟵ نصّ "HH:mm". قيمة غير صالحة تُقرأ null (بلا نافذة) لا صفراً.</summary>
    private static string? ReadTime(System.Data.Common.DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return reader.GetValue(ordinal) is TimeSpan span
            ? $"{span.Hours:00}:{span.Minutes:00}"
            : null;
    }

    /// <summary>نصّ "HH:mm" ⟶ عمود time. الفارغ أو غير الصالح يُكتب NULL — «بلا نافذة».</summary>
    private static object ToTime(string? value) =>
        TimeSpan.TryParse(value, out var span) ? span : DBNull.Value;
}
