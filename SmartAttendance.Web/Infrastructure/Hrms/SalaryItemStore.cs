using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// كتالوج عناصر الراتب (نمط كيان «عناصر الراتب» + ZenHR salary components) — يرقّي
/// فكرة lookup العلاوات القديمة لكيان كامل: لكل عنصر نوع (دخل/اقتطاع/أوفرتايم/علاوة)،
/// طريقة قيمة (ثابت/معادلة)، خاضع للضريبة؟، ضمن الإجمالي؟، نسبي (prorated بالحضور)؟.
/// محرك المسير (PayrollRunStore) يبني بنود القسيمة من هذا الكتالوج + علاوات الموظف.
/// عناصر النظام (الراتب الأساسي، ضريبة الدخل، الضمان) مبذورة ومحميّة من الحذف.
/// </summary>
public static class SalaryItemStore
{
    /// <summary>أنواع العنصر (المفتاح ← التسمية العربية).</summary>
    public static readonly (string Key, string Label)[] ItemTypes =
    {
        ("Income", "دخل / بدل"),
        ("Deduction", "اقتطاع"),
        ("Overtime", "عمل إضافي"),
        ("Statutory", "استقطاع نظامي (ضريبة/ضمان)")
    };

    public static readonly (string Key, string Label)[] ValueKinds =
    {
        ("Fixed", "قيمة ثابتة"),
        ("Formula", "معادلة"),
        ("PerEmployee", "لكل موظف (من ملفه المالي)")
    };

    /// <summary>متغيّرات المعادلة المتاحة ببوابة المعادلة (مفتاح ← تسمية) — تُعرض كرقاقات قابلة للإدراج.</summary>
    public static readonly (string Key, string Label)[] FormulaVars =
    {
        ("Basic", "الراتب الأساسي"),
        ("Allowances", "إجمالي العلاوات"),
        ("Gross", "الراتب الإجمالي"),
        ("Hours", "عدد الساعات"),
        ("Days", "عدد الأيام"),
        ("DailyRate", "الأجر اليومي"),
        ("HourlyRate", "الأجر الساعي"),
    };

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    public sealed class SalaryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string ItemType { get; set; } = "Income";        // Income | Deduction | Overtime | Statutory
        public string ValueKind { get; set; } = "Fixed";        // Fixed | Formula | PerEmployee
        public decimal DefaultValue { get; set; }               // مبلغ ثابت حسب ValueKind
        public string? Formula { get; set; }                    // تعبير المعادلة (عند ValueKind=Formula) مثل: Basic / 30 / 8 * Hours
        public bool Taxable { get; set; } = true;               // يدخل بوعاء الضريبة؟
        public bool InGross { get; set; } = true;               // يدخل بالراتب الإجمالي؟
        public bool Prorated { get; set; }                      // يُنسّب حسب أيام الحضور؟
        public bool IsSystem { get; set; }                      // عنصر نظام محمي من الحذف
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public string ItemTypeLabel => LabelOf(ItemTypes, ItemType);
        public string ValueKindLabel => LabelOf(ValueKinds, ValueKind);
        public bool IsAddition => ItemType is "Income" or "Overtime";
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('SalaryItems', 'U') IS NULL
BEGIN
    CREATE TABLE SalaryItems
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        ItemType nvarchar(20) NOT NULL DEFAULT(N'Income'),
        ValueKind nvarchar(20) NOT NULL DEFAULT(N'Fixed'),
        DefaultValue decimal(18,4) NOT NULL DEFAULT(0),
        Taxable bit NOT NULL DEFAULT(1),
        InGross bit NOT NULL DEFAULT(1),
        Prorated bit NOT NULL DEFAULT(0),
        IsSystem bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        SortOrder int NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- عناصر النظام المحميّة (تُبذر مرة واحدة) — الأساسي مصدره الملف المالي للموظف
IF NOT EXISTS (SELECT 1 FROM SalaryItems WHERE IsSystem = 1)
BEGIN
    INSERT INTO SalaryItems (Name, NameEn, ItemType, ValueKind, DefaultValue, Taxable, InGross, Prorated, IsSystem, SortOrder)
    VALUES
      (N'الراتب الأساسي', N'Basic Salary', N'Income',    N'PerEmployee', 0, 1, 1, 1, 1, 0),
      (N'ضريبة الدخل',    N'Income Tax',   N'Statutory', N'Formula',     0, 0, 0, 0, 1, 90),
      (N'الضمان الاجتماعي (حصة الموظف)', N'Social Security (Employee)', N'Statutory', N'Formula', 0, 0, 0, 0, 1, 91);
END;

-- بوابة المعادلة (idempotent)
IF COL_LENGTH('SalaryItems','Formula') IS NULL ALTER TABLE SalaryItems ADD Formula nvarchar(500) NULL;
""");
    }

    public static async Task<List<SalaryItem>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM SalaryItems ORDER BY SortOrder, Name;",
            command => { },
            Read);
    }

    /// <summary>عناصر «الدخل/البدل» النشطة القابلة للإسناد لعلاوات الموظف.</summary>
    public static async Task<List<SalaryItem>> ActiveIncomeItemsAsync(ApplicationDbContext dbContext)
    {
        var all = await ListAsync(dbContext);
        return all.Where(x => x.IsActive && x.ItemType is "Income" or "Overtime").ToList();
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, SalaryItem item)
    {
        await EnsureAsync(dbContext);

        if (item.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE SalaryItems
SET Name = @Name, NameEn = @NameEn, ItemType = @ItemType, ValueKind = @ValueKind,
    DefaultValue = @DefaultValue, Formula = @Formula, Taxable = @Taxable, InGross = @InGross,
    Prorated = @Prorated, IsActive = @IsActive, SortOrder = @SortOrder
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", item.Id);
                    AddParameters(command, item);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO SalaryItems (Name, NameEn, ItemType, ValueKind, DefaultValue, Formula, Taxable, InGross, Prorated, IsSystem, IsActive, SortOrder)
VALUES (@Name, @NameEn, @ItemType, @ValueKind, @DefaultValue, @Formula, @Taxable, @InGross, @Prorated, 0, @IsActive, @SortOrder);
""",
                command => AddParameters(command, item));
        }
    }

    /// <summary>الحذف ممنوع لعناصر النظام.</summary>
    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM SalaryItems WHERE Id = @Id AND IsSystem = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static SalaryItem Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Name = HrmsDatabase.GetString(reader, "Name"),
        NameEn = HrmsDatabase.GetString(reader, "NameEn") is { Length: > 0 } en ? en : null,
        ItemType = HrmsDatabase.GetString(reader, "ItemType") is { Length: > 0 } t ? t : "Income",
        ValueKind = HrmsDatabase.GetString(reader, "ValueKind") is { Length: > 0 } v ? v : "Fixed",
        DefaultValue = reader["DefaultValue"] is decimal d ? d : 0,
        Formula = HrmsDatabase.GetString(reader, "Formula") is { Length: > 0 } fx ? fx : null,
        Taxable = HrmsDatabase.GetBool(reader, "Taxable"),
        InGross = HrmsDatabase.GetBool(reader, "InGross"),
        Prorated = HrmsDatabase.GetBool(reader, "Prorated"),
        IsSystem = HrmsDatabase.GetBool(reader, "IsSystem"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
    };

    private static void AddParameters(System.Data.Common.DbCommand command, SalaryItem item)
    {
        HrmsDatabase.AddParameter(command, "@Name", item.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)item.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ItemType", item.ItemType);
        HrmsDatabase.AddParameter(command, "@ValueKind", item.ValueKind);
        HrmsDatabase.AddParameter(command, "@DefaultValue", item.DefaultValue);
        HrmsDatabase.AddParameter(command, "@Formula", (object?)item.Formula ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Taxable", item.Taxable ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@InGross", item.InGross ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Prorated", item.Prorated ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@IsActive", item.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@SortOrder", item.SortOrder);
    }
}
