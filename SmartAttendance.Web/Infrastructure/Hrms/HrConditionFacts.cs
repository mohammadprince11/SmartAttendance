using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// بناء «حقائق» الموظف التي يُقيَّم عليها <see cref="HrConditions"/>.
///
/// الفصل مقصود: <see cref="Build"/> **نقيّ وقابل للاختبار** بلا قاعدة بيانات، و
/// <see cref="LoadAsync"/> يجلب الصفوف فقط. فأي خطأ بمنطق العمر أو مدة الخدمة
/// يُمسَك باختبار وحدة لا بتجربة حيّة على 1356 موظفاً.
///
/// ⚠️ **الحقائق تُبنى بتاريخ مرجعي صريح** (<c>asOf</c>) لا بـ<c>DateTime.Now</c>:
/// «مدة الخدمة أكبر من سنة» جوابها يختلف بحسب الشهر المحتسَب، ونسخة سياسة تُقيَّم
/// اليوم بنتيجة ثم تُعاد بشهر ماضٍ بنتيجة أخرى = فرق مالي بلا تفسير.
/// </summary>
public static class HrConditionFacts
{
    /// <summary>صفّ الموظف كما يُقرأ من القاعدة — كل ما يلزم لبناء الحقائق ولا شيء زائد.</summary>
    public sealed record EmployeeRow
    {
        public int Id { get; init; }
        public string EmployeeNo { get; init; } = string.Empty;
        public int BranchId { get; init; }
        public int DepartmentId { get; init; }
        public int? CompanyId { get; init; }
        public int? PositionId { get; init; }
        public int? DirectManagerId { get; init; }

        public string? Gender { get; init; }
        public string? MaritalStatus { get; init; }
        public string? Religion { get; init; }
        public string? Nationality { get; init; }
        public string? Country { get; init; }
        public DateOnly? BirthDate { get; init; }
        public bool IsCitizen { get; init; }

        public string? ContractType { get; init; }
        public string? WorkType { get; init; }
        public string? JobGrade { get; init; }
        public string? EmploymentStatus { get; init; }
        public bool IsActive { get; init; }
        public DateOnly HireDate { get; init; }
        public DateOnly? JoiningDate { get; init; }
        public DateOnly? ContractEndDate { get; init; }

        public string? SponsorName { get; init; }

        public decimal? BasicSalary { get; init; }
        public string? SalaryScale { get; init; }
        public string? SocialSecurityType { get; init; }

        /// <summary>مجموع العلاوات من <c>EmployeeCompensations</c> — مصدرٌ مختلف عن الأساسي.</summary>
        public decimal? Allowances { get; init; }
        public string? BankName { get; init; }

        /// <summary>قيم الحقول الإضافية بمفتاحها الخام (بلا بادئة <c>custom:</c>).</summary>
        public IReadOnlyDictionary<string, string> CustomFields { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// العمر بالسنوات الكاملة عند التاريخ المرجعي. تاريخ ميلاد مستقبليّ (خطأ إدخال)
    /// يعطي حقيقةً مفقودة لا عمراً سالباً — فلا يطابق شرطاً ولا يدخل استثناءً بالخطأ.
    /// </summary>
    public static int? AgeYears(DateOnly? birthDate, DateOnly asOf)
    {
        if (birthDate is not { } born || born > asOf)
        {
            return null;
        }

        var years = asOf.Year - born.Year;
        if (asOf < born.AddYears(years))
        {
            years--;
        }

        return years;
    }

    /// <summary>
    /// مدة الخدمة بالأشهر الكاملة. الأساس <c>JoiningDate</c> إن وُجد وإلا
    /// <c>HireDate</c> — نفس تمييز كيان بين «المتعاقَد عليه» و«من باشر فعلاً»،
    /// والمباشرة الفعلية هي ما يُبنى عليه الاستحقاق.
    /// </summary>
    public static int? ServiceMonths(DateOnly hireDate, DateOnly? joiningDate, DateOnly asOf)
    {
        var start = joiningDate ?? hireDate;
        if (start > asOf)
        {
            return null;
        }

        var months = ((asOf.Year - start.Year) * 12) + asOf.Month - start.Month;
        if (asOf.Day < start.Day)
        {
            months--;
        }

        return Math.Max(months, 0);
    }

    /// <summary>يبني قاموس الحقائق لموظف واحد — مفاتيحه مفاتيح <see cref="HrConditionCatalog"/>.</summary>
    public static Dictionary<string, HrConditions.Fact> Build(EmployeeRow row, DateOnly asOf)
    {
        var facts = new Dictionary<string, HrConditions.Fact>(StringComparer.OrdinalIgnoreCase)
        {
            ["branch"] = HrConditions.Fact.Of(row.BranchId == 0 ? null : row.BranchId),
            ["department"] = HrConditions.Fact.Of(row.DepartmentId == 0 ? null : row.DepartmentId),
            ["company"] = HrConditions.Fact.Of(row.CompanyId),
            ["position"] = HrConditions.Fact.Of(row.PositionId),
            ["manager"] = HrConditions.Fact.Of(row.DirectManagerId),

            ["gender"] = HrConditions.Fact.Of(row.Gender),
            ["maritalstatus"] = HrConditions.Fact.Of(row.MaritalStatus),
            ["religion"] = HrConditions.Fact.Of(row.Religion),
            ["nationality"] = HrConditions.Fact.Of(row.Nationality),
            ["country"] = HrConditions.Fact.Of(row.Country),
            ["age"] = HrConditions.Fact.Of(AgeYears(row.BirthDate, asOf)),
            ["iscitizen"] = HrConditions.Fact.Of(row.IsCitizen),

            ["contracttype"] = HrConditions.Fact.Of(row.ContractType),
            ["worktype"] = HrConditions.Fact.Of(row.WorkType),
            ["jobgrade"] = HrConditions.Fact.Of(row.JobGrade),
            ["employmentstatus"] = HrConditions.Fact.Of(row.EmploymentStatus),
            ["isactive"] = HrConditions.Fact.Of(row.IsActive),
            ["hiredate"] = HrConditions.Fact.Of(row.HireDate),
            ["joiningdate"] = HrConditions.Fact.Of(row.JoiningDate),
            ["contractenddate"] = HrConditions.Fact.Of(row.ContractEndDate),
            ["servicemonths"] = HrConditions.Fact.Of(ServiceMonths(row.HireDate, row.JoiningDate, asOf)),

            ["sponsor"] = HrConditions.Fact.Of(row.SponsorName),

            ["basicsalary"] = HrConditions.Fact.Of(row.BasicSalary),
            ["allowances"] = HrConditions.Fact.Of(row.Allowances),
            // الإجمالي **مشتقّ لا مخزَّن**: أساسيٌّ بلا علاوات إجماليُّه الأساسي،
            // لكن غياب الأساسي نفسه يعني إجمالياً **مجهولاً** لا صفراً — وإلا طابق
            // «الراتب الكلي أقل من كذا» كلَّ من لا راتب مسجَّل له (1355 موظفاً).
            ["grosssalary"] = HrConditions.Fact.Of(
                row.BasicSalary is null ? null : row.BasicSalary + (row.Allowances ?? 0)),
            ["salaryscale"] = HrConditions.Fact.Of(row.SalaryScale),
            ["gosiType"] = HrConditions.Fact.Of(row.SocialSecurityType),
            ["bank"] = HrConditions.Fact.Of(row.BankName),

            ["employee"] = HrConditions.Fact.Of(row.Id),
            ["employeecode"] = HrConditions.Fact.Of(row.EmployeeNo)
        };

        foreach (var (key, value) in row.CustomFields)
        {
            facts[HrConditionCatalog.CustomFieldPrefix + key] = HrConditions.Fact.Of(value);
        }

        return facts;
    }

    // ── القراءة من القاعدة ─────────────────────────────────────────────────────

    /// <summary>
    /// يقرأ صفوف الموظفين اللازمة للتقييم. <paramref name="employeeId"/> يقصر القراءة
    /// على موظف واحد (المسار الشائع: حسم سياسة لموظف بعينه)، وإغفاله يقرأ الجميع
    /// (المسار الجماعي: من تنطبق عليه هذه النسخة؟).
    ///
    /// الحقول الإضافية تُقرأ باستعلام ثانٍ لا بـJOIN، لأن الربط يضاعف صفوف الموظف
    /// بعدد حقوله فتصير قراءة 1356 موظفاً آلافَ الصفوف بلا داعٍ.
    /// </summary>
    public static async Task<List<EmployeeRow>> LoadAsync(
        ApplicationDbContext db,
        int? employeeId = null,
        int? companyId = null,
        CompanyScope? authorizationScope = null)
    {
        if (authorizationScope?.IsDeniedAll == true) return new();
        var filter = employeeId is null ? string.Empty : " AND e.Id = @EmployeeId";
        var companyFilter = companyId is null ? string.Empty : " AND e.CompanyId = @CompanyId";
        var authorizationFilter = authorizationScope is null
            ? "1=1"
            : EmployeeCompanyGuard.ListFilter(authorizationScope, "e.CompanyId");

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT e.Id, e.EmployeeNo, e.BranchId, e.DepartmentId, e.CompanyId, e.PositionId,
       e.DirectManagerId, e.Gender, e.MaritalStatus, e.Religion, e.Nationality,
       e.Country, e.BirthDate, ISNULL(e.IsCitizen, 1) AS IsCitizen,
       e.ContractType, e.WorkType, e.JobGrade, e.EmploymentStatus, e.IsActive,
       e.HireDate, e.JoiningDate, e.ContractEndDate, e.SponsorName,
       fi.BasicSalary, fi.SalaryScale, fi.SocialSecurityType,
       ec.Allowances, ec.BankName
FROM Employees e
LEFT JOIN EmployeeFinancialInfos fi
       ON fi.EmployeeId = e.Id AND ISNULL(fi.IsDeleted, 0) = 0
LEFT JOIN EmployeeCompensations ec ON ec.EmployeeId = e.Id
WHERE ISNULL(e.IsDeleted, 0) = 0 AND {authorizationFilter}{filter}{companyFilter};
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (companyId is not null) HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            },
            reader => new EmployeeRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                CompanyId = HrmsDatabase.GetNullableInt(reader, "CompanyId"),
                PositionId = HrmsDatabase.GetNullableInt(reader, "PositionId"),
                DirectManagerId = HrmsDatabase.GetNullableInt(reader, "DirectManagerId"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                MaritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus"),
                Religion = HrmsDatabase.GetString(reader, "Religion"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                BirthDate = HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                IsCitizen = HrmsDatabase.GetBool(reader, "IsCitizen"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType"),
                JobGrade = HrmsDatabase.GetString(reader, "JobGrade"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate") ?? default,
                JoiningDate = HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                SponsorName = HrmsDatabase.GetString(reader, "SponsorName"),
                BasicSalary = HrmsDatabase.GetNullableDecimal(reader, "BasicSalary"),
                SalaryScale = HrmsDatabase.GetString(reader, "SalaryScale"),
                SocialSecurityType = HrmsDatabase.GetString(reader, "SocialSecurityType"),
                Allowances = HrmsDatabase.GetNullableDecimal(reader, "Allowances"),
                BankName = HrmsDatabase.GetString(reader, "BankName")
            });

        await AttachCustomFieldsAsync(db, rows, employeeId, companyId, authorizationScope);
        return rows;
    }

    private static async Task AttachCustomFieldsAsync(
        ApplicationDbContext db,
        List<EmployeeRow> rows,
        int? employeeId,
        int? companyId,
        CompanyScope? authorizationScope)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var employeeFilter = employeeId is null ? string.Empty : " AND f.EmployeeId = @EmployeeId";
        var companyFilter = companyId is null ? string.Empty : " AND e.CompanyId = @CompanyId";
        var authorizationFilter = authorizationScope is null
            ? "1=1"
            : EmployeeCompanyGuard.ListFilter(authorizationScope, "e.CompanyId");

        var values = await HrmsDatabase.QueryAsync(
            db,
            $"SELECT f.EmployeeId, f.FieldKey, f.FieldValue FROM EmployeeCustomFields f INNER JOIN Employees e ON e.Id=f.EmployeeId WHERE {authorizationFilter}{employeeFilter}{companyFilter};",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (companyId is not null) HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            },
            reader => (
                EmployeeId: HrmsDatabase.GetInt(reader, "EmployeeId"),
                Key: HrmsDatabase.GetString(reader, "FieldKey"),
                Value: HrmsDatabase.GetString(reader, "FieldValue")));

        if (values.Count == 0)
        {
            return;
        }

        var byEmployee = values
            .Where(entry => entry.Key.Length > 0)
            .GroupBy(entry => entry.EmployeeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group
                    .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(entry => entry.Key, entry => entry.Last().Value, StringComparer.OrdinalIgnoreCase));

        for (var index = 0; index < rows.Count; index++)
        {
            if (byEmployee.TryGetValue(rows[index].Id, out var fields))
            {
                rows[index] = rows[index] with { CustomFields = fields };
            }
        }
    }

    /// <summary>الحقول الإضافية المعرَّفة فعلاً — تغذّي منسدلة المعايير بالواجهة.</summary>
    public static async Task<List<(string Key, string Label)>> LoadCustomFieldDefinitionsAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT DISTINCT FieldKey, ISNULL(MAX(FieldLabel), FieldKey) AS FieldLabel
FROM EmployeeCustomFields
GROUP BY FieldKey;
""",
            command => { },
            reader => (
                HrmsDatabase.GetString(reader, "FieldKey"),
                HrmsDatabase.GetString(reader, "FieldLabel")));
}
