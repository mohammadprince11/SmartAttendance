using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// القيم الثابتة المسمّاة بصيغ الرواتب — نظير «قيم ثابتة»
/// (<c>SalaryItems?PageType=10</c>) بكيان: جدول اسم ↔ قيمة يُستعمل داخل الصيغ.
///
/// <b>لماذا تهمّ:</b> بلا هذا الجدول يُكتب الرقم السحري داخل كل صيغة على حدة
/// (الحدّ الأدنى للأجور · سقف راتب الضمان · معامل ساعة الأوفرتايم)، وتغييره
/// يعني تتبّع عشرات الصيغ يدوياً — وأيّ صيغةٍ تُنسى تبقى تحسب بالرقم القديم بصمت.
///
/// <b>قيد الاسم:</b> مُقيِّم الصيغ يقرأ المعرّف بـ<c>char.IsLetter</c> فقط، فالمفتاح
/// حروفٌ لا غير (بلا أرقام ولا شرطات ولا فراغات). التحقّق هنا لا بالواجهة وحدها،
/// وإلا حُفظ ثابتٌ لا تراه أي صيغة أبداً.
/// </summary>
public static class SalaryConstantStore
{
    public sealed class SalaryConstant
    {
        public int Id { get; set; }
        /// <summary>المفتاح كما يُكتب بالصيغة — حروف فقط.</summary>
        public string Key { get; set; } = string.Empty;
        /// <summary>الاسم المعروض بالعربية.</summary>
        public string Name { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string? Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// مفتاح صالح: حرف واحد على الأقل، وحروفٌ فقط، ولا يصادم متغيّراً مبنيّاً.
    /// المصادمة تُرفض لا تُقبل بأولوية — «ثابتٌ اسمه Basic» يجعل قراءة أي صيغة كذبة.
    /// </summary>
    public static string? Validate(string? key)
    {
        var trimmed = (key ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return "المفتاح إلزامي.";

        if (!trimmed.All(char.IsLetter))
            return "المفتاح حروف فقط بلا أرقام أو فراغات أو رموز — هذا قيد مُقيِّم الصيغ.";

        if (PayrollFormulaVariables.Catalog.Any(v => string.Equals(v.Key, trimmed, StringComparison.OrdinalIgnoreCase)))
            return $"«{trimmed}» متغيّر مبنيّ بفضاء الرواتب — اختر اسماً آخر.";

        return null;
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext) =>
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('SalaryConstants', 'U') IS NULL
BEGIN
    CREATE TABLE SalaryConstants
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Key] nvarchar(60) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Value decimal(18,4) NOT NULL DEFAULT(0),
        Note nvarchar(300) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_SalaryConstants_Key ON SalaryConstants ([Key]);
END;
""");

    public static async Task<List<SalaryConstant>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT Id, [Key], Name, Value, Note, IsActive FROM SalaryConstants ORDER BY [Key];",
            command => { },
            reader => new SalaryConstant
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Key = HrmsDatabase.GetString(reader, "Key"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Value = reader["Value"] is decimal d ? d : 0m,
                Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
    }

    /// <summary>الثوابت النشطة كقاموس جاهز للدمج بفضاء المتغيّرات.</summary>
    public static async Task<Dictionary<string, decimal>> ActiveMapAsync(ApplicationDbContext dbContext)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await ListAsync(dbContext))
        {
            if (row.IsActive) map[row.Key] = row.Value;
        }
        return map;
    }

    /// <returns>رسالة خطأ أو <c>null</c> عند النجاح.</returns>
    public static async Task<string?> SaveAsync(ApplicationDbContext dbContext, SalaryConstant item)
    {
        if (Validate(item.Key) is { } error) return error;
        if (string.IsNullOrWhiteSpace(item.Name)) return "الاسم إلزامي.";

        await EnsureAsync(dbContext);

        var key = item.Key.Trim();
        var clash = await HrmsDatabase.ScalarAsync<int?>(
            dbContext,
            "SELECT TOP 1 Id FROM SalaryConstants WHERE [Key] = @Key AND Id <> @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Id", item.Id);
            });
        if (clash is > 0) return $"المفتاح «{key}» مستعمل بثابتٍ آخر.";

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            item.Id > 0
                ? "UPDATE SalaryConstants SET [Key]=@Key, Name=@Name, Value=@Value, Note=@Note, IsActive=@Active WHERE Id=@Id;"
                : "INSERT INTO SalaryConstants ([Key], Name, Value, Note, IsActive) VALUES (@Key, @Name, @Value, @Note, @Active);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", item.Id);
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Name", item.Name.Trim());
                HrmsDatabase.AddParameter(command, "@Value", item.Value);
                HrmsDatabase.AddParameter(command, "@Note", (object?)item.Note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Active", item.IsActive ? 1 : 0);
            });

        return null;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM SalaryConstants WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>تنسيق القيمة للعرض بلا اعتماد ثقافة الخادم.</summary>
    public static string Format(decimal value) => value.ToString("#,0.####", CultureInfo.InvariantCulture);
}
