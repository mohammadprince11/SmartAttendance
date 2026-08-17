using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سلم الرواتب — نظير «إدارة سلم الرواتب» بكيان (أبعاد ثلاثة + فئات + مجموعات).
///
/// نموذجنا أبسط عمداً: **درجة** بكود واسم وثلاثة أبعاد نصية اختيارية (Dim1/2/3 تُسمّى
/// من التهيئة، مثل الفئة/الدرجة/المرتبة) وفئة/مجموعة للتصنيف، ومدى أساسي (أدنى/متوسط/
/// أقصى). الجدول قاموسٌ وأرقام؛ **الاقتراح يُعرض بالملف المالي ولا يُفرض** — لا مساس
/// بأي صيغة احتساب (قاعدة الرواتب الحمراء). الجدول بهجرة محكومة 20260816-02.
/// </summary>
public static class SalaryScaleStore
{
    public const string KeyDim1Label = "Payroll.SalaryScale.Dim1Label";
    public const string KeyDim2Label = "Payroll.SalaryScale.Dim2Label";
    public const string KeyDim3Label = "Payroll.SalaryScale.Dim3Label";

    public sealed class Grade
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Dim1 { get; set; }
        public string? Dim2 { get; set; }
        public string? Dim3 { get; set; }
        public string? Category { get; set; }
        public string? GroupName { get; set; }
        public decimal? MinBasic { get; set; }
        public decimal? MidBasic { get; set; }
        public decimal? MaxBasic { get; set; }
        public string? Color { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public string Label => string.IsNullOrWhiteSpace(Name) ? Code : $"{Code} — {Name}";

        /// <summary>هل يقع الأساسي داخل مدى الدرجة؟ null-tolerant: بلا حدود = داخل.</summary>
        public bool Contains(decimal basic) =>
            (MinBasic is null || basic >= MinBasic) && (MaxBasic is null || basic <= MaxBasic);
    }

    /// <summary>
    /// درجات السلم المرئية لهذا النطاق: المشتركة (CompanyId IS NULL) + ما يخصّ شركاته —
    /// نمط عزل التهيئة 8D–8M عبر <see cref="Security.CompanyScope.ToSharedConfigSqlPredicate"/>.
    /// </summary>
    public static async Task<List<Grade>> ListAsync(ApplicationDbContext db, Security.CompanyScope scope, bool activeOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Code, Name, Dim1, Dim2, Dim3, Category, GroupName, MinBasic, MidBasic, MaxBasic, Color, SortOrder, IsActive
FROM SalaryScaleGrades
WHERE {scope.ToSharedConfigSqlPredicate("CompanyId")}
{(activeOnly ? "AND IsActive = 1" : "")}
ORDER BY SortOrder, Code;
""",
            command => { },
            reader => new Grade
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Dim1 = N(HrmsDatabase.GetString(reader, "Dim1")),
                Dim2 = N(HrmsDatabase.GetString(reader, "Dim2")),
                Dim3 = N(HrmsDatabase.GetString(reader, "Dim3")),
                Category = N(HrmsDatabase.GetString(reader, "Category")),
                GroupName = N(HrmsDatabase.GetString(reader, "GroupName")),
                MinBasic = reader["MinBasic"] is decimal mn ? mn : null,
                MidBasic = reader["MidBasic"] is decimal md ? md : null,
                MaxBasic = reader["MaxBasic"] is decimal mx ? mx : null,
                Color = N(HrmsDatabase.GetString(reader, "Color")),
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
    }

    /// <summary>
    /// حفظ درجة (إدراج/تحديث). التحديث بحارس ملكية مغلق الفشل (المعرّف خارج النطاق =
    /// «غير موجود»)، والإدراج يُنسب لشركة النطاق إن كانت واحدة (مشترك للأدمن).
    /// يعيد رسالة رفض للمدخل الفاسد.
    /// </summary>
    public static async Task<(bool Ok, string Message)> SaveAsync(ApplicationDbContext db, Security.CompanyScope scope, Grade g)
    {
        ArgumentNullException.ThrowIfNull(scope);
        g.Code = (g.Code ?? string.Empty).Trim();
        g.Name = (g.Name ?? string.Empty).Trim();
        if (g.Code.Length == 0) return (false, "كود الدرجة مطلوب.");

        if (g.Id > 0 && !await Security.ConfigTenantScope.IsInScopeAsync(db, Security.ConfigTenantScope.SalaryScaleGrades, g.Id, scope))
            return (false, "الدرجة غير موجودة.");
        if (g.MinBasic is > 0 && g.MaxBasic is > 0 && g.MinBasic > g.MaxBasic)
            return (false, "الحد الأدنى للأساسي أكبر من الأقصى — راجع المدى.");
        if (g.MidBasic is > 0 && ((g.MinBasic is > 0 && g.MidBasic < g.MinBasic) || (g.MaxBasic is > 0 && g.MidBasic > g.MaxBasic)))
            return (false, "المتوسط خارج المدى (أدنى–أقصى).");

        var owningCompany = Security.ConfigTenantScope.OwningCompany(scope);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF EXISTS (SELECT 1 FROM SalaryScaleGrades WHERE Code = @Code AND ISNULL(CompanyId, 0) = ISNULL(@Company, 0) AND (@Id = 0 OR Id <> @Id))
    THROW 51000, 'DUPLICATE_CODE', 1;

IF @Id > 0
    UPDATE SalaryScaleGrades SET Code=@Code, Name=@Name, Dim1=@D1, Dim2=@D2, Dim3=@D3, Category=@Cat, GroupName=@Grp,
        MinBasic=@Min, MidBasic=@Mid, MaxBasic=@Max, Color=@Color, SortOrder=@Sort, IsActive=@Active
    WHERE Id=@Id;
ELSE
    INSERT INTO SalaryScaleGrades (Code, Name, Dim1, Dim2, Dim3, Category, GroupName, MinBasic, MidBasic, MaxBasic, Color, SortOrder, IsActive, CompanyId)
    VALUES (@Code, @Name, @D1, @D2, @D3, @Cat, @Grp, @Min, @Mid, @Max, @Color, @Sort, @Active, @Company);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", g.Id);
                HrmsDatabase.AddParameter(command, "@Company", (object?)owningCompany ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Code", g.Code);
                HrmsDatabase.AddParameter(command, "@Name", g.Name);
                HrmsDatabase.AddParameter(command, "@D1", (object?)N(g.Dim1) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@D2", (object?)N(g.Dim2) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@D3", (object?)N(g.Dim3) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Cat", (object?)N(g.Category) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Grp", (object?)N(g.GroupName) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Min", (object?)g.MinBasic ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Mid", (object?)g.MidBasic ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Max", (object?)g.MaxBasic ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Color", (object?)N(g.Color) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Sort", g.SortOrder);
                HrmsDatabase.AddParameter(command, "@Active", g.IsActive);
            });

        return (true, "حُفظت الدرجة.");
    }

    /// <summary>حذف بحارس ملكية مغلق الفشل — خارج النطاق = لا شيء يُحذف ويعود false.</summary>
    public static async Task<bool> DeleteAsync(ApplicationDbContext db, Security.CompanyScope scope, int id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!await Security.ConfigTenantScope.IsInScopeAsync(db, Security.ConfigTenantScope.SalaryScaleGrades, id, scope))
            return false;
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM SalaryScaleGrades WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        return true;
    }

    /// <summary>الدرجة المقترحة لأساسي معيّن — أول درجة نشطة يقع الأساسي بمداها (بترتيب السلم).</summary>
    public static Grade? Suggest(IEnumerable<Grade> grades, decimal basic) =>
        grades.Where(g => g.IsActive && (g.MinBasic.HasValue || g.MaxBasic.HasValue) && g.Contains(basic))
              .OrderBy(g => g.SortOrder).ThenBy(g => g.Code)
              .FirstOrDefault();

    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
