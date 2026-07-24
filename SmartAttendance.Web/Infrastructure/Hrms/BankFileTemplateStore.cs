using System.Globalization;
using System.Text;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قوالب ملفات البنوك (نمط كيان — تصدير المسير للبنك بتنسيق كل بنك): كل قالب يحدّد
/// الأعمدة وترتيبها ورؤوسها المخصصة والفاصل وإدراج صف الترويسة، فوق صفوف ملف البنك
/// (<see cref="PayrollRunStore.BankFileRowsAsync"/>). self-healing + قوالب مبذورة
/// «تحتاج تأكيد التنسيق الفعلي للبنك».
/// </summary>
public static class BankFileTemplateStore
{
    /// <summary>الحقول المتاحة لبناء عمود بالقالب (المفتاح ← التسمية الافتراضية).</summary>
    public static readonly (string Key, string Label)[] Fields =
    {
        ("no", "الرقم الوظيفي"),
        ("name", "اسم الموظف"),
        ("method", "طريقة الدفع"),
        ("bank", "البنك"),
        ("branch", "فرع البنك"),
        ("iban", "الآيبان"),
        ("card", "رقم البطاقة"),
        ("net", "صافي الراتب"),
        ("payable", "قابل للتحويل")
    };

    public static readonly (string Key, string Label, string Char)[] Delimiters =
    {
        ("Comma", "فاصلة (,)", ","),
        ("Semicolon", "فاصلة منقوطة (;)", ";"),
        ("Tab", "جدولة (Tab)", "\t")
    };

    public static string LabelOf(string key) => Fields.FirstOrDefault(f => f.Key == key).Label ?? key;
    public static string DelimiterChar(string key) => Delimiters.FirstOrDefault(d => d.Key == key).Char ?? ",";

    public sealed class Template
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string Delimiter { get; set; } = "Comma";
        public bool IncludeHeader { get; set; } = true;
        public string ColumnsCsv { get; set; } = string.Empty;     // مفاتيح الحقول مرتّبة
        public string? HeadersCsv { get; set; }                    // رؤوس مخصصة موازية (فارغ = الافتراضي)
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;

        public List<string> Columns => ColumnsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        public List<string> Headers => (HeadersCsv ?? string.Empty)
            .Split('|', StringSplitOptions.None).ToList();

        /// <summary>رأس العمود i: المخصص إن وُجد، وإلا التسمية الافتراضية للحقل.</summary>
        public string HeaderFor(int index, string fieldKey)
        {
            var headers = Headers;
            if (index < headers.Count && !string.IsNullOrWhiteSpace(headers[index])) return headers[index].Trim();
            return LabelOf(fieldKey);
        }
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('BankFileTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE BankFileTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        BankName nvarchar(150) NULL,
        Delimiter nvarchar(20) NOT NULL DEFAULT(N'Comma'),
        IncludeHeader bit NOT NULL DEFAULT(1),
        ColumnsCsv nvarchar(500) NOT NULL DEFAULT(N''),
        HeadersCsv nvarchar(1000) NULL,
        IsDefault bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );

    -- قوالب مبذورة (⚠️ تحتاج تأكيد التنسيق الفعلي لكل بنك)
    INSERT INTO BankFileTemplates (Name, BankName, Delimiter, IncludeHeader, ColumnsCsv, IsDefault)
    VALUES
      (N'افتراضي (عام)', NULL, N'Comma', 1, N'no,name,method,bank,branch,iban,card,net,payable', 1),
      (N'الرافدين (نموذج)', N'مصرف الرافدين', N'Comma', 1, N'no,name,iban,net', 0),
      (N'الرشيد (نموذج)', N'مصرف الرشيد', N'Comma', 1, N'no,name,card,net', 0);
END;
""");
    }

    public static async Task<List<Template>> ListAsync(ApplicationDbContext db)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.QueryAsync(
            db,
            "SELECT * FROM BankFileTemplates ORDER BY IsDefault DESC, Name;",
            command => { },
            Read);
    }

    public static async Task<List<Template>> ActiveAsync(ApplicationDbContext db) =>
        (await ListAsync(db)).Where(t => t.IsActive).ToList();

    public static async Task<Template?> GetAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        return (await HrmsDatabase.QueryAsync(
            db,
            "SELECT * FROM BankFileTemplates WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            Read)).FirstOrDefault();
    }

    public static async Task<Template?> DefaultAsync(ApplicationDbContext db) =>
        (await ActiveAsync(db)).FirstOrDefault(t => t.IsDefault) ?? (await ActiveAsync(db)).FirstOrDefault();

    public static async Task<(bool Ok, string Message)> SaveAsync(ApplicationDbContext db, Template t)
    {
        await EnsureAsync(db);
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "اسم القالب مطلوب.");
        if (t.Columns.Count == 0) return (false, "اختر عموداً واحداً على الأقل.");

        // قالب افتراضي واحد فقط
        if (t.IsDefault)
            await HrmsDatabase.ExecuteAsync(db, "UPDATE BankFileTemplates SET IsDefault = 0 WHERE Id <> @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", t.Id));

        if (t.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE BankFileTemplates SET Name=@Name, BankName=@Bank, Delimiter=@Delim, IncludeHeader=@Header,
    ColumnsCsv=@Cols, HeadersCsv=@Heads, IsDefault=@Default, IsActive=@Active WHERE Id=@Id;
""",
                command => { HrmsDatabase.AddParameter(command, "@Id", t.Id); Add(command, t); });
            return (true, "تم تحديث القالب.");
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO BankFileTemplates (Name, BankName, Delimiter, IncludeHeader, ColumnsCsv, HeadersCsv, IsDefault, IsActive)
VALUES (@Name, @Bank, @Delim, @Header, @Cols, @Heads, @Default, @Active);
""",
            command => Add(command, t));
        return (true, "أُنشئ القالب.");
    }

    public static async Task DeleteAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM BankFileTemplates WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>بناء محتوى ملف البنك لدفعة حسب القالب (نص CSV/DSV).</summary>
    public static string BuildContent(Template template, IEnumerable<PayrollRunStore.BankFileRow> rows)
    {
        var delim = DelimiterChar(template.Delimiter);
        var columns = template.Columns;
        var sb = new StringBuilder();

        if (template.IncludeHeader)
            sb.AppendLine(string.Join(delim, columns.Select((c, i) => Escape(template.HeaderFor(i, c), delim))));

        foreach (var row in rows)
            sb.AppendLine(string.Join(delim, columns.Select(c => Escape(Value(row, c), delim))));

        return sb.ToString();
    }

    private static string Value(PayrollRunStore.BankFileRow r, string key) => key switch
    {
        "no" => r.EmployeeNo,
        "name" => r.EmployeeName,
        "method" => r.PaymentMethod,
        "bank" => r.BankName,
        "branch" => r.BankBranch,
        "iban" => r.Iban,
        "card" => r.CardNo,
        "net" => r.NetSalary.ToString("0.00", CultureInfo.InvariantCulture),
        "payable" => r.IsPayable ? "نعم" : "لا",
        _ => string.Empty
    };

    private static string Escape(string value, string delim)
    {
        var text = value ?? string.Empty;
        var needsQuote = text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            || (delim.Length == 1 && text.Contains(delim[0]));
        return needsQuote ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static Template Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Name = HrmsDatabase.GetString(reader, "Name"),
        BankName = HrmsDatabase.GetString(reader, "BankName") is { Length: > 0 } bn ? bn : null,
        Delimiter = HrmsDatabase.GetString(reader, "Delimiter") is { Length: > 0 } d ? d : "Comma",
        IncludeHeader = HrmsDatabase.GetBool(reader, "IncludeHeader"),
        ColumnsCsv = HrmsDatabase.GetString(reader, "ColumnsCsv"),
        HeadersCsv = HrmsDatabase.GetString(reader, "HeadersCsv") is { Length: > 0 } h ? h : null,
        IsDefault = HrmsDatabase.GetBool(reader, "IsDefault"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive")
    };

    private static void Add(System.Data.Common.DbCommand command, Template t)
    {
        HrmsDatabase.AddParameter(command, "@Name", t.Name.Trim());
        HrmsDatabase.AddParameter(command, "@Bank", (object?)t.BankName ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Delim", t.Delimiter);
        HrmsDatabase.AddParameter(command, "@Header", t.IncludeHeader ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Cols", string.Join(",", t.Columns));
        HrmsDatabase.AddParameter(command, "@Heads", string.IsNullOrWhiteSpace(t.HeadersCsv) ? DBNull.Value : t.HeadersCsv);
        HrmsDatabase.AddParameter(command, "@Default", t.IsDefault ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Active", t.IsActive ? 1 : 0);
    }
}
