using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

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

    /// <summary>
    /// متغيّرات المعادلة المتاحة ببوابة المعادلة (مفتاح ← تسمية) — تُعرض كرقاقات
    /// قابلة للإدراج. <b>مشتقّة من <see cref="PayrollFormulaVariables.Catalog"/>
    /// لا مكتوبة يدوياً</b>: قائمةٌ موازية تعني أن تُعرض للمستخدم رقاقةٌ لا
    /// يعرفها المسير (أو العكس).
    /// </summary>
    public static readonly (string Key, string Label)[] FormulaVars =
        PayrollFormulaVariables.Catalog.Select(v => (v.Key, v.Label)).ToArray();

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    public sealed class SalaryItem
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string ItemType { get; set; } = "Income";        // Income | Deduction | Overtime | Statutory
        public string ValueKind { get; set; } = "Fixed";        // Fixed | Formula | PerEmployee
        public decimal DefaultValue { get; set; }               // مبلغ ثابت حسب ValueKind
        public string? Formula { get; set; }                    // تعبير المعادلة (عند ValueKind=Formula) مثل: Basic / 30 / 8 * Hours
        public bool Taxable { get; set; } = true;               // يدخل بوعاء الضريبة؟ (خضوع لكل علاوة)
        public bool GosiEligible { get; set; } = true;          // يدخل بوعاء الضمان؟ (خضوع لكل علاوة)
        public bool InGross { get; set; } = true;               // يدخل بالراتب الإجمالي؟
        public bool Prorated { get; set; }                      // يُنسّب حسب أيام الحضور؟ (= حسّاس لوعاء الحضور)
        public bool OvertimeEligible { get; set; }              // يدخل وعاء الأوفرتايم؟
        public bool UnpaidLeaveEligible { get; set; }           // يدخل وعاء الإجازة غير المدفوعة؟
        public bool IsSystem { get; set; }                      // عنصر نظام محمي من الحذف
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        // ── تبويب «القواعد» (نمط كيان): كلها اختيارية · null ⟹ السلوك القديم بلا أثر ──
        public decimal? MinValue { get; set; }                  // القيمة الدنيا (حدّ سفليّ للقيمة المحتسبة)
        public decimal? MaxValue { get; set; }                  // القيمة القصوى (حدّ علويّ)
        public DateOnly? ValidFrom { get; set; }                // فترة الصلاحية — البداية (شامل)
        public DateOnly? ValidTo { get; set; }                  // فترة الصلاحية — النهاية (شامل)

        // ── تبويب «معايير الاستحقاق»: شروطٌ على سمات الموظف · فارغ ⟹ مؤهّل للجميع ──
        public string? EligibilityJson { get; set; }            // HrConditions.ConditionSet مُسلسَل

        public string ItemTypeLabel => LabelOf(ItemTypes, ItemType);
        public string ValueKindLabel => LabelOf(ValueKinds, ValueKind);
        public bool IsAddition => ItemType is "Income" or "Overtime";

        /// <summary>الشروط المؤهِّلة (مُفكَّكة من <see cref="EligibilityJson"/>).</summary>
        public HrConditions.ConditionSet Eligibility => HrConditions.Deserialize(EligibilityJson);

        /// <summary>هل العنصر ساري المفعول ضمن فترة المسير؟ null ⟹ دائماً (سلوك قديم).</summary>
        public bool WithinValidity(DateOnly periodStart, DateOnly periodEnd) =>
            (ValidFrom is null || ValidFrom.Value <= periodEnd) &&
            (ValidTo is null || ValidTo.Value >= periodStart);

        /// <summary>هل الموظف مؤهّل لهذا العنصر؟ شروط فارغة ⟹ الكل (سلوك قديم).</summary>
        public bool EligibleFor(IReadOnlyDictionary<string, HrConditions.Fact> facts) =>
            HrConditions.Matches(Eligibility, facts, matchWhenEmpty: true);

        /// <summary>حصر القيمة بين الحدّ الأدنى والأقصى إن وُجدا (بلا حدّ ⟹ بلا أثر).</summary>
        public decimal Clamp(decimal value)
        {
            if (MinValue is { } lo && value < lo) value = lo;
            if (MaxValue is { } hi && value > hi) value = hi;
            return value;
        }
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
        CompanyId int NULL,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        ItemType nvarchar(20) NOT NULL DEFAULT(N'Income'),
        ValueKind nvarchar(20) NOT NULL DEFAULT(N'Fixed'),
        DefaultValue decimal(18,4) NOT NULL DEFAULT(0),
        Taxable bit NOT NULL DEFAULT(1),
        GosiEligible bit NOT NULL DEFAULT(1),
        InGross bit NOT NULL DEFAULT(1),
        Prorated bit NOT NULL DEFAULT(0),
        OvertimeEligible bit NOT NULL DEFAULT(0),
        UnpaidLeaveEligible bit NOT NULL DEFAULT(0),
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

-- تبويب «القواعد» + «معايير الاستحقاق» (idempotent · كلها اختيارية بلا افتراض يغيّر السلوك)
IF COL_LENGTH('SalaryItems','MinValue')        IS NULL ALTER TABLE SalaryItems ADD MinValue decimal(18,4) NULL;
IF COL_LENGTH('SalaryItems','MaxValue')        IS NULL ALTER TABLE SalaryItems ADD MaxValue decimal(18,4) NULL;
IF COL_LENGTH('SalaryItems','ValidFrom')       IS NULL ALTER TABLE SalaryItems ADD ValidFrom date NULL;
IF COL_LENGTH('SalaryItems','ValidTo')         IS NULL ALTER TABLE SalaryItems ADD ValidTo date NULL;
IF COL_LENGTH('SalaryItems','EligibilityJson') IS NULL ALTER TABLE SalaryItems ADD EligibilityJson nvarchar(max) NULL;
""");
    }

    public static async Task<List<SalaryItem>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int? companyId = null)
    {
        await EnsureAsync(dbContext);
        if (scope.IsDeniedAll || companyId is > 0 && !scope.Allows(companyId)) return new();

        var companyPredicate = companyId is > 0
            ? "CompanyId = @CompanyId"
            : scope.ToSqlPredicate("CompanyId");
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT * FROM SalaryItems
WHERE (IsSystem = 1 AND CompanyId IS NULL)
   OR (IsSystem = 0 AND {companyPredicate})
ORDER BY SortOrder, Name;
""",
            command => HrmsDatabase.AddParameter(
                command, "@CompanyId", (object?)companyId ?? DBNull.Value),
            Read);
    }

    /// <summary>عناصر «الدخل/البدل» النشطة القابلة للإسناد لعلاوات الموظف.</summary>
    public static async Task<List<SalaryItem>> ActiveIncomeItemsAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int? companyId = null)
    {
        var all = await ListAsync(dbContext, scope, companyId);
        return all.Where(x => x.IsActive && x.ItemType is "Income" or "Overtime").ToList();
    }

    public static async Task<bool> SaveAsync(
        ApplicationDbContext dbContext, CompanyScope scope, SalaryItem item)
    {
        await EnsureAsync(dbContext);
        if (item.CompanyId is not > 0 || !scope.Allows(item.CompanyId)) return false;

        if (item.Id > 0)
        {
            return await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
UPDATE SalaryItems
SET Name = @Name, NameEn = @NameEn, ItemType = @ItemType, ValueKind = @ValueKind,
    DefaultValue = @DefaultValue, Formula = @Formula, Taxable = @Taxable, GosiEligible = @GosiEligible, InGross = @InGross,
    Prorated = @Prorated, OvertimeEligible = @OvertimeEligible, UnpaidLeaveEligible = @UnpaidLeaveEligible,
    IsActive = @IsActive, SortOrder = @SortOrder,
    MinValue = @MinValue, MaxValue = @MaxValue, ValidFrom = @ValidFrom, ValidTo = @ValidTo, EligibilityJson = @EligibilityJson
WHERE Id = @Id AND IsSystem = 0 AND CompanyId = @CompanyId;
SELECT @@ROWCOUNT;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", item.Id);
                    AddParameters(command, item);
                }) > 0;
        }
        else
        {
            return await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO SalaryItems (CompanyId, Name, NameEn, ItemType, ValueKind, DefaultValue, Formula, Taxable, GosiEligible, InGross, Prorated, OvertimeEligible, UnpaidLeaveEligible, IsSystem, IsActive, SortOrder, MinValue, MaxValue, ValidFrom, ValidTo, EligibilityJson)
VALUES (@CompanyId, @Name, @NameEn, @ItemType, @ValueKind, @DefaultValue, @Formula, @Taxable, @GosiEligible, @InGross, @Prorated, @OvertimeEligible, @UnpaidLeaveEligible, 0, @IsActive, @SortOrder, @MinValue, @MaxValue, @ValidFrom, @ValidTo, @EligibilityJson);
SELECT @@ROWCOUNT;
""",
                command => AddParameters(command, item)) > 0;
        }
    }

    /// <summary>الحذف ممنوع لعناصر النظام.</summary>
    public static async Task<bool> DeleteAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int id)
    {
        await EnsureAsync(dbContext);
        if (scope.IsDeniedAll) return false;
        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"DELETE FROM SalaryItems WHERE Id = @Id AND IsSystem = 0 AND {scope.ToSqlPredicate("CompanyId")}; SELECT @@ROWCOUNT;",
            command => HrmsDatabase.AddParameter(command, "@Id", id)) > 0;
    }

    private static SalaryItem Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        CompanyId = reader["CompanyId"] is int companyId ? companyId : null,
        Name = HrmsDatabase.GetString(reader, "Name"),
        NameEn = HrmsDatabase.GetString(reader, "NameEn") is { Length: > 0 } en ? en : null,
        ItemType = HrmsDatabase.GetString(reader, "ItemType") is { Length: > 0 } t ? t : "Income",
        ValueKind = HrmsDatabase.GetString(reader, "ValueKind") is { Length: > 0 } v ? v : "Fixed",
        DefaultValue = reader["DefaultValue"] is decimal d ? d : 0,
        Formula = HrmsDatabase.GetString(reader, "Formula") is { Length: > 0 } fx ? fx : null,
        Taxable = HrmsDatabase.GetBool(reader, "Taxable"),
        GosiEligible = HrmsDatabase.GetBool(reader, "GosiEligible"),
        InGross = HrmsDatabase.GetBool(reader, "InGross"),
        Prorated = HrmsDatabase.GetBool(reader, "Prorated"),
        OvertimeEligible = HrmsDatabase.GetBool(reader, "OvertimeEligible"),
        UnpaidLeaveEligible = HrmsDatabase.GetBool(reader, "UnpaidLeaveEligible"),
        IsSystem = HrmsDatabase.GetBool(reader, "IsSystem"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        SortOrder = HrmsDatabase.GetInt(reader, "SortOrder"),
        MinValue = reader["MinValue"] is decimal mn ? mn : null,
        MaxValue = reader["MaxValue"] is decimal mx ? mx : null,
        ValidFrom = HrmsDatabase.GetDateOnly(reader, "ValidFrom"),
        ValidTo = HrmsDatabase.GetDateOnly(reader, "ValidTo"),
        EligibilityJson = HrmsDatabase.GetString(reader, "EligibilityJson") is { Length: > 0 } ej ? ej : null
    };

    private static void AddParameters(System.Data.Common.DbCommand command, SalaryItem item)
    {
        HrmsDatabase.AddParameter(command, "@CompanyId", item.CompanyId!.Value);
        HrmsDatabase.AddParameter(command, "@Name", item.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)item.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ItemType", item.ItemType);
        HrmsDatabase.AddParameter(command, "@ValueKind", item.ValueKind);
        HrmsDatabase.AddParameter(command, "@DefaultValue", item.DefaultValue);
        HrmsDatabase.AddParameter(command, "@Formula", (object?)item.Formula ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Taxable", item.Taxable ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@GosiEligible", item.GosiEligible ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@InGross", item.InGross ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Prorated", item.Prorated ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@OvertimeEligible", item.OvertimeEligible ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@UnpaidLeaveEligible", item.UnpaidLeaveEligible ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@IsActive", item.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@SortOrder", item.SortOrder);
        HrmsDatabase.AddParameter(command, "@MinValue", (object?)item.MinValue ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@MaxValue", (object?)item.MaxValue ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ValidFrom", item.ValidFrom is { } vf ? vf.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ValidTo", item.ValidTo is { } vt ? vt.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        HrmsDatabase.AddParameter(command, "@EligibilityJson", (object?)item.EligibilityJson ?? DBNull.Value);
    }
}
