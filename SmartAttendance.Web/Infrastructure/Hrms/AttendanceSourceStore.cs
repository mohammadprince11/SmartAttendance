using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مصادر بيانات الحضور (نمط كيان — قسم 3.3 بدراسة الحضور): من أين تأتي البصمات
/// الخام. ثلاثة أنواع قراءة: ملف إكسل · عرض/جدول بقاعدة بيانات جهاز البصمة ·
/// API خارجي. «يستخدم الدلالات» يحدد إن كان المصدر يصنّف بصماته بالدلالات.
/// يُبذر مصدر «ملف إكسل» النظامي الممثل للاستيراد الحالي (AttendanceImports).
/// </summary>
public static class AttendanceSourceStore
{
    /// <summary>أنواع القراءة (المفتاح ← التسمية العربية) — نفس أنواع كيان الثلاثة.</summary>
    public static readonly (string Key, string Label)[] ReadTypes =
    {
        ("Excel", "ملف إكسل"),
        ("DbView", "عرض/جدول قاعدة بيانات"),
        ("Api", "واجهة API خارجية")
    };

    public static string ReadTypeLabel(string key) =>
        ReadTypes.FirstOrDefault(t => t.Key == key).Label ?? key;

    public sealed class AttendanceSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ReadType { get; set; } = "Excel";   // Excel | DbView | Api
        public string? ConfigValue { get; set; }          // اسم العرض/الجدول أو عنوان الـ API
        public bool UsesSemantics { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AttendanceSources', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceSources
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        ReadType nvarchar(20) NOT NULL DEFAULT(N'Excel'),
        ConfigValue nvarchar(300) NULL,
        UsesSemantics bit NOT NULL DEFAULT(0),
        IsSystem bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM AttendanceSources WHERE IsSystem = 1)
BEGIN
    INSERT INTO AttendanceSources (Name, ReadType, ConfigValue, UsesSemantics, IsSystem, IsActive)
    VALUES (N'استيراد إكسل (النظام الحالي)', N'Excel', N'/AttendanceImports', 0, 1, 1);
END;
""");
    }

    public static async Task<List<AttendanceSource>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM AttendanceSources ORDER BY Name;",
            command => { },
            reader => new AttendanceSource
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                ReadType = HrmsDatabase.GetString(reader, "ReadType") is { Length: > 0 } t ? t : "Excel",
                ConfigValue = HrmsDatabase.GetString(reader, "ConfigValue") is { Length: > 0 } c ? c : null,
                UsesSemantics = HrmsDatabase.GetBool(reader, "UsesSemantics"),
                IsSystem = HrmsDatabase.GetBool(reader, "IsSystem"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, AttendanceSource source)
    {
        await EnsureAsync(dbContext);

        if (source.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE AttendanceSources
SET Name = @Name, ReadType = @ReadType, ConfigValue = @Config,
    UsesSemantics = @Semantics, IsActive = @Active
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", source.Id);
                    AddParameters(command, source);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AttendanceSources (Name, ReadType, ConfigValue, UsesSemantics, IsSystem, IsActive)
VALUES (@Name, @ReadType, @Config, @Semantics, 0, @Active);
""",
                command => AddParameters(command, source));
        }
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM AttendanceSources WHERE Id = @Id AND IsSystem = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static void AddParameters(System.Data.Common.DbCommand command, AttendanceSource source)
    {
        HrmsDatabase.AddParameter(command, "@Name", source.Name);
        HrmsDatabase.AddParameter(command, "@ReadType", source.ReadType);
        HrmsDatabase.AddParameter(command, "@Config", (object?)source.ConfigValue ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Semantics", source.UsesSemantics ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Active", source.IsActive ? 1 : 0);
    }
}
