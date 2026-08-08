using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرك المسير (نمط كيان «حساب الرواتب» + ZenHR Salary Batches): دفعة شهرية لها
/// دورة حياة Draft ← Calculated ← Locked ← Issued ← PayslipSent. «الاحتساب» يبني
/// لكل موظف سطراً: الأساسي (من الملف المالي) + العلاوات النشطة، مُنسّباً حسب أيام
/// الحضور من الاعتماد الشهري (EmployeeMonthAttendance)، مطروحاً منه الضريبة
/// التصاعدية والضمان (PayrollConfigStore) وخصومات المخالفات (DeductionAmount +
/// أثر لائحة الجزاءات أيام/ساعات). كل سطر يحمل بنوده التفصيلية للقسيمة.
/// </summary>
public static class PayrollRunStore
{
    public static readonly string[] Lifecycle = { "Draft", "Calculated", "Locked", "Issued", "PayslipSent" };

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "مسودة",
        "Calculated" => "محتسب",
        "Locked" => "مقفل",
        "Issued" => "معتمد للصرف",
        "PayslipSent" => "أُرسلت القسائم",
        _ => status
    };

    public sealed class PayrollRun
    {
        public int Id { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public string Status { get; set; } = "Draft";
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalGosiCompany { get; set; }
        public string? CalculatedBy { get; set; }
        public DateTime? CalculatedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>كيف حُدِّد النطاق (توثيقي) — الحساب يعتمد صفوف النطاق نفسها.</summary>
        public string ScopeMode { get; set; } = PayrollRunScope.ModeAll;

        /// <summary>عدد موظفي النطاق؛ صفر ⟹ التشغيل يشمل كل الموظفين النشطين.</summary>
        public int ScopeCount { get; set; }

        /// <summary>
        /// الشركة المالكة للدفعة — حدّ العزل. <c>null</c> للدفعات السابقة للعمود
        /// وللمختلطة التي تعذّرت نسبتها، ولا يراها إلا غير المقيَّد.
        /// </summary>
        public int? CompanyId { get; set; }

        public string StatusLabelText => StatusLabel(Status);
        public string ScopeText => PayrollRunScope.Describe(ScopeMode, ScopeCount);
        public string PeriodText => $"{Month:00}/{Year}";
    }

    public sealed class PayrollLine
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public decimal BasicSalary { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal GosiEmployee { get; set; }
        public decimal GosiCompany { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal NetSalary { get; set; }
        public int WorkDays { get; set; }
        public int AbsentDays { get; set; }
        public List<Component> Components { get; set; } = new();

        public decimal TotalDeductions => TaxAmount + GosiEmployee + OtherDeductions;
        public decimal EmployerCost => GrossSalary + GosiCompany;
        public IEnumerable<Component> Earnings => Components.Where(c => c.IsAddition);
        public IEnumerable<Component> Deductions => Components.Where(c => !c.IsAddition);
    }

    public sealed class Component
    {
        public string ItemName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsAddition { get; set; }
        public string Kind { get; set; } = string.Empty;   // Basic|Allowance|Income|Overtime|SalaryDays|LeaveEncashment|Formula|Deduction|Leave|Tax|Gosi|Penalty
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PayrollRuns', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRuns
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BatchNo nvarchar(30) NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT(N'Draft'),
        ScopeMode nvarchar(20) NULL,
        EmployeeCount int NOT NULL DEFAULT(0),
        TotalGross decimal(18,2) NOT NULL DEFAULT(0),
        TotalNet decimal(18,2) NOT NULL DEFAULT(0),
        TotalTax decimal(18,2) NOT NULL DEFAULT(0),
        TotalGosiCompany decimal(18,2) NOT NULL DEFAULT(0),
        CalculatedBy nvarchar(150) NULL,
        CalculatedAt datetime2 NULL,
        LockedAt datetime2 NULL,
        IssuedAt datetime2 NULL,
        PayslipSentAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('PayrollRunLines', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunLines
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId int NOT NULL,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NOT NULL DEFAULT(0),
        TotalAllowances decimal(18,2) NOT NULL DEFAULT(0),
        GrossSalary decimal(18,2) NOT NULL DEFAULT(0),
        TaxAmount decimal(18,2) NOT NULL DEFAULT(0),
        GosiEmployee decimal(18,2) NOT NULL DEFAULT(0),
        GosiCompany decimal(18,2) NOT NULL DEFAULT(0),
        OtherDeductions decimal(18,2) NOT NULL DEFAULT(0),
        NetSalary decimal(18,2) NOT NULL DEFAULT(0),
        WorkDays int NOT NULL DEFAULT(0),
        AbsentDays int NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_PayrollRunLines_Run ON PayrollRunLines (RunId);
END;

IF OBJECT_ID('PayrollRunLineComponents', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunLineComponents
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LineId int NOT NULL,
        ItemName nvarchar(200) NOT NULL,
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        IsAddition bit NOT NULL DEFAULT(1),
        Kind nvarchar(30) NOT NULL DEFAULT(N'Allowance')
    );
    CREATE INDEX IX_PayrollRunLineComponents_Line ON PayrollRunLineComponents (LineId);
END;

-- سجلّ المستبعَدين (نظير تبويب «الرواتب المستثناة» بكيان): كنّا نعدّ المتخطَّين
-- عدّاً مجمّعاً بسطر رسالة، فسؤال «لماذا لم يُحتسب فلان؟» بلا جواب إلا بقراءة الكود.
-- الآن كل استبعاد يُسجَّل باسم صاحبه وسببه وسبيل معالجته.
IF OBJECT_ID('PayrollRunExclusions', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunExclusions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId int NOT NULL,
        EmployeeId int NOT NULL,
        ReasonCode nvarchar(40) NOT NULL,
        Reason nvarchar(300) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_PayrollRunExclusions_Run ON PayrollRunExclusions (RunId);
END;
""");
    }

    // ═══════════════════════ عزل الشركات بالمسير ═══════════════════════
    //
    // كانت دفعات المسير بلا حدّ شركة إطلاقاً: القائمة تعرض دفعات الجميع، وكل نقطة
    // تأخذ `runId` من الطلب (احتساب · قفل · إصدار · **إرسال قسائم بالبريد** · حذف ·
    // **تصدير ملف البنك بالآيبانات**) تعمل على أي دفعة بأي شركة. تصحيحُ ذلك يحتاج
    // ثلاثة أشياء معاً — ونقص أيٍّ منها يُبقي الثغرة:
    //   1. نسبة الدفعة لشركة (هجرة `20260807-04/05`).
    //   2. حصر موظفي الاحتساب بتلك الشركة (`CalculateAsync`).
    //   3. فحص ملكية قبل كل عملية (`CanAccessRunAsync` أدناه).

    /// <summary>
    /// دفعات المسير ضمن نطاق شركات المستخدم. الشرط يُبنى من أعداد صحيحة موثوقة
    /// (انظر <see cref="CompanyScope.ToSqlPredicate"/>) لا من مدخل مستخدم.
    /// </summary>
    public static async Task<List<PayrollRun>> ListRunsAsync(
        ApplicationDbContext dbContext, CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);

        if (scope.IsDeniedAll) return new List<PayrollRun>();

        // الدفعة بلا شركة (تاريخية أو مختلطة) لغير المقيَّد وحده — نفس قاعدة
        // `CompanyScope.Allows(null)`، مطبَّقة هنا بالـSQL كي لا تُقرأ أصلاً.
        var predicate = scope.IsUnrestricted
            ? "1 = 1"
            : scope.ToSqlPredicate("r.CompanyId");

        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT r.*, (SELECT COUNT(1) FROM PayrollRunScopeMembers s WHERE s.RunId = r.Id) AS ScopeCount
FROM PayrollRuns r
WHERE {predicate}
ORDER BY r.[Year] DESC, r.[Month] DESC, r.Id DESC;
""",
            command => { },
            ReadRun);
    }

    /// <summary>
    /// شركة الدفعة عند أول احتساب: تُشتقّ من أعضاء نطاقها إن اتفقوا على شركة واحدة،
    /// وتُثبَّت على الصفّ فلا تُشتقّ ثانيةً. اختلافهم ⟹ <c>null</c> والدفعة تبقى
    /// على السلوك القديم — لا نُسند شركةً بالتخمين لصفٍّ ماليّ.
    /// </summary>
    private static async Task<int?> ResolveRunCompanyAsync(
        ApplicationDbContext dbContext, int runId)
    {
        var companies = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT DISTINCT e.CompanyId
FROM PayrollRunScopeMembers s
INNER JOIN Employees e ON e.Id = s.EmployeeId
WHERE s.RunId = @Run AND e.CompanyId IS NOT NULL;
""",
            command => HrmsDatabase.AddParameter(command, "@Run", runId),
            reader => HrmsDatabase.GetNullableInt(reader, "CompanyId"));

        if (companies.Count != 1 || companies[0] is not { } companyId) return null;

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE PayrollRuns SET CompanyId = @Company WHERE Id = @Run AND CompanyId IS NULL;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Company", companyId);
                HrmsDatabase.AddParameter(command, "@Run", runId);
            });

        return companyId;
    }

    /// <summary>
    /// بوابة كل عملية على دفعة. <b>مغلقة الفشل</b>: دفعة غير موجودة، أو شركتها
    /// خارج النطاق، أو بلا شركة لمستخدمٍ مقيَّد ⟹ <c>false</c>.
    ///
    /// <para>تُستدعى من الصفحة قبل استدعاء أي عملية بالمتجر — والحارس النصّي
    /// <c>PayrollCompanyIsolationTests</c> يفشل إن ظهرت نقطة تتجاوزها.</para>
    /// </summary>
    public static async Task<bool> CanAccessRunAsync(
        ApplicationDbContext dbContext, int runId, CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (runId <= 0 || scope.IsDeniedAll) return false;
        if (scope.IsUnrestricted) return true;

        await EnsureAsync(dbContext);

        var companyId = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT CompanyId FROM PayrollRuns WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", runId),
            reader => HrmsDatabase.GetNullableInt(reader, "CompanyId"));

        // لا صفّ ⟹ الدفعة غير موجودة ⟹ رفض (لا نميّز «غير موجودة» عن «ممنوعة»
        // فلا يُستدلّ على وجود دفعات شركةٍ أخرى بعدّ الاستجابات).
        return companyId.Count == 1 && scope.Allows(companyId[0]);
    }

    public static async Task<PayrollRun?> GetRunAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        return (await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, (SELECT COUNT(1) FROM PayrollRunScopeMembers s WHERE s.RunId = r.Id) AS ScopeCount
FROM PayrollRuns r
WHERE r.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            ReadRun)).FirstOrDefault();
    }

    /// <summary>
    /// إنشاء دفعة مسير جديدة لشهر (رقم دفعة yyyy-M-seq نمط كيان).
    /// <paramref name="scopeEmployeeIds"/> فارغة/null ⟹ الدفعة تشمل كل النشطين.
    /// </summary>
    public static async Task<(bool Ok, string Message, int RunId)> CreateRunAsync(
        ApplicationDbContext dbContext, int year, int month,
        string? scopeMode = null, IEnumerable<int>? scopeEmployeeIds = null,
        int? companyId = null)
    {
        await EnsureAsync(dbContext);
        if (year < 2000 || month is < 1 or > 12) return (false, "شهر غير صالح.", 0);

        var seq = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(1) FROM PayrollRuns WHERE [Year] = @Y AND [Month] = @M;",
            command => { HrmsDatabase.AddParameter(command, "@Y", year); HrmsDatabase.AddParameter(command, "@M", month); }) + 1;

        var ids = (scopeEmployeeIds ?? Enumerable.Empty<int>()).Distinct().ToList();
        var mode = PayrollRunScope.NormalizeMode(scopeMode);
        if (ids.Count == 0) mode = PayrollRunScope.ModeAll;   // نطاق بلا أعضاء = الكل

        var batchNo = $"{year}-{month}-{seq}";
        var id = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "INSERT INTO PayrollRuns (BatchNo, [Year], [Month], Status, ScopeMode, CompanyId) VALUES (@Batch, @Y, @M, N'Draft', @Scope, @Company); SELECT CAST(SCOPE_IDENTITY() AS int);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Batch", batchNo);
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@Scope", mode);
                HrmsDatabase.AddParameter(command, "@Company", (object?)companyId ?? DBNull.Value);
            });

        if (ids.Count > 0) await PayrollRunScopeStore.ReplaceAsync(dbContext, id, ids);

        var scopeText = ids.Count > 0
            ? $" — النطاق: {PayrollRunScope.Describe(mode, ids.Count)}"
            : " — النطاق: كل الموظفين النشطين";
        return (true, $"أُنشئت دفعة {batchNo}{scopeText}. شغّل «الاحتساب».", id);
    }

    /// <summary>حساب المسير: يبني السطور والبنود. مسموح فقط على Draft/Calculated.</summary>
    public static async Task<(bool Ok, string Message)> CalculateAsync(
        ApplicationDbContext dbContext, int runId, string userName)
    {
        var run = await GetRunAsync(dbContext, runId);
        if (run == null) return (false, "الدفعة غير موجودة.");
        if (run.Status is "Locked" or "Issued" or "PayslipSent")
            return (false, "لا يمكن إعادة احتساب دفعة مقفلة/معتمدة.");

        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);
        await EmployeeAllowanceSchema.EnsureAsync(dbContext);
        await MonthAttendanceStore.EnsureAsync(dbContext);
        await ViolationCaseSchema.EnsureAsync(dbContext);

        // ترحيل أقساط القروض المستحقة لهذه الفترة تلقائياً كحركات اقتطاع قبل الاحتساب —
        // كانت خطوة يدوية منفصلة بصفحة القروض تُنسى فتضيع خصومات القروض بصمت. النداء
        // idempotent: يرحّل الأقساط غير المرحّلة فقط ولقروض معتمدة فقط، فإعادة الاحتساب
        // لا تُكرّر الخصم، وتُلتقط الحركة الناتجة ضمن حركات الاقتطاع أدناه.
        // نطاق الترحيل = شركة الدفعة لا كل الشركات: مستخدمٌ مقيَّد يحتسب دفعة شركته
        // كان — عبر هذا المسار — يرحّل أقساط قروض موظفي كل الشركات (نفس ثغرة زر
        // «ترحيل الأقساط» بصفحة القروض، من باب خلفيّ). دفعةٌ بلا شركة (تاريخية) تبقى
        // على السلوك القديم للتوافق مع نشرٍ أحادي الشركة.
        var runCompanyForLoans = run.CompanyId ?? await ResolveRunCompanyAsync(dbContext, runId);
        var loanScope = runCompanyForLoans is > 0
            ? CompanyScope.ForCompanies(new[] { runCompanyForLoans.Value })
            : CompanyScope.Unrestricted();
        await LoanStore.EnsureAsync(dbContext);
        await LoanStore.PostDueInstallmentsAsync(dbContext, loanScope, run.Year, run.Month, userName);

        // سياسة ربط الراتب بالحضور تُقرأ مرّة للتشغيل كلّه.
        var linkPolicy = await AttendanceSalaryLinkSettings.LoadAsync(dbContext);
        var linkMode = linkPolicy.Mode;

        // ملفات الضريبة/الضمان **كلّها** لا الملف النشط وحده: الملف صار خاصيةً لكل
        // موظف (إسناد صريح أو شرط) ⟵ PayrollProfileResolver. من لا إسناد له ولا شرط
        // ينطبق عليه يأخذ الملف النشط تماماً كما قبل هذا التغيير.
        var taxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(dbContext);
        var gosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(dbContext);
        var taxById = taxProfiles.ToDictionary(profile => profile.Id);
        var gosiById = gosiProfiles.ToDictionary(profile => profile.Id);
        var taxCandidates = PayrollConfigStore.Candidates(taxProfiles);
        var gosiCandidates = PayrollConfigStore.Candidates(gosiProfiles);

        // عضوية أوعية **كل** الملفات دفعةً واحدة (قاموس بالذاكرة) بدل نداء لكل موظف.
        var baseMembers = await SalaryBaseStore.AllAsync(dbContext);

        var periodStart = new DateOnly(run.Year, run.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        // حقائق الموظفين لتقييم شروط الملفات — بتاريخ مرجعي صريح (نهاية الفترة) لا
        // بتاريخ اليوم، وإلا اختلف حسمُ الملف بإعادة احتساب شهرٍ ماضٍ.
        // ولا تُقرأ أصلاً ما لم يوجد ملفٌ مشروط: قراءة 1356 موظفاً بحقولهم الإضافية
        // ثمنٌ لا يُدفع لميزة لم تُستعمل بعد.
        var hasConditionalProfiles = taxCandidates.Concat(gosiCandidates)
            .Any(candidate => candidate.IsActive && candidate.Conditions is { IsEmpty: false });

        var factsByEmployee = new Dictionary<int, Dictionary<string, HrConditions.Fact>>();
        if (hasConditionalProfiles)
        {
            foreach (var row in await HrConditionFacts.LoadAsync(dbContext))
            {
                factsByEmployee[row.Id] = HrConditionFacts.Build(row, periodEnd);
            }
        }

        var noFacts = new Dictionary<string, HrConditions.Fact>();

        // --- مدخلات: موظفون + ملف مالي + علاوات + حضور شهري + خصومات مخالفات ---
        // ⚠️ **حدّ العزل الحقيقي للمسير.** كان هذا الاستعلام يختار موظفي **كل
        // الشركات** (`WHERE IsDeleted=0 AND IsActive=1` بلا شيء آخر)، فدفعة شركة
        // تولّد قسائم لموظفي شركةٍ أخرى — لا بخطأ استخدام بل بالسلوك الافتراضي.
        //
        // الآن يُحصر بشركة الدفعة. ودفعةٌ بلا شركة (تاريخية أو مسوّدة لم تُنسَب بعد)
        // تأخذ شركتها **من أول احتساب** — وحتى ذلك الحين تبقى على السلوك القديم
        // للتوافق مع نشرٍ أحادي الشركة، فلا ينكسر مسير قائم عند الترقية.
        var runCompanyId = run.CompanyId ?? await ResolveRunCompanyAsync(dbContext, runId);

        var companyFilter = runCompanyId is > 0 ? " AND e.CompanyId = @Company" : string.Empty;

        var employees = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName FROM Employees e WHERE ISNULL(e.IsDeleted,0)=0 AND ISNULL(e.IsActive,1)=1{companyFilter} ORDER BY e.EmployeeNo;",
            command => { if (runCompanyId is > 0) HrmsDatabase.AddParameter(command, "@Company", runCompanyId.Value); },
            reader => new { Id = HrmsDatabase.GetInt(reader, "Id"), No = HrmsDatabase.GetString(reader, "EmployeeNo"), Name = HrmsDatabase.GetString(reader, "FullName") });

        // نطاق التشغيل محفوظ مع الدفعة: إعادة الاحتساب بعد شهر تلتزم بنفس النطاق.
        // لا صفوف ⟹ كل النشطين — القرار بالكود (PayrollRunScope) لا بصفٍّ افتراضي.
        var scope = await PayrollRunScopeStore.IdsAsync(dbContext, runId);
        var scopeSet = new HashSet<int>(scope);
        var outsideScope = PayrollRunScope.OutsideCandidates(scope, employees.Select(e => e.Id).ToList()).Count;
        var candidates = employees.Where(e => PayrollRunScope.Includes(scopeSet, e.Id)).ToList();

        var financial = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ISNULL(BasicSalary,0) AS BasicSalary, ISNULL(StopSalaryCalc,0) AS StopSalaryCalc, TaxProfileId, GosiProfileId FROM EmployeeFinancialInfos WHERE ISNULL(IsDeleted,0)=0;",
            command => { },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Basic = reader["BasicSalary"] is decimal b ? b : 0,
                Stop = HrmsDatabase.GetBool(reader, "StopSalaryCalc"),
                TaxProfileId = HrmsDatabase.GetNullableInt(reader, "TaxProfileId"),
                GosiProfileId = HrmsDatabase.GetNullableInt(reader, "GosiProfileId")
            }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var allowances = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ItemName, ISNULL(Amount,0) AS Amount, FromDate, ToDate, ISNULL(EndAfterDate,0) AS EndAfterDate FROM EmployeeAllowances WHERE ISNULL(IsDeleted,0)=0;",
            command => { },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                From = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                To = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                EndAfter = HrmsDatabase.GetBool(reader, "EndAfterDate")
            }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // بناء الاعتماد الشهري من الحضور اليومي المحلَّل تلقائياً قبل قراءته — نفس منطق
        // ترحيل أقساط القروض أعلاه: كان البناء خطوة يدوية بشاشة الاعتماد الشهري تُنسى،
        // فيقرأ المسير جدولاً فارغاً/متقادماً ⟹ معامل الحضور = 1 فلا يُخصم الغياب ولا
        // الإجازة بلا راتب بصمت (استحقاق زائد). النداء idempotent وآمن للبوابة: MERGE
        // يحدّث صفوف UnderReview فقط ويُدرج الناقصين، ولا يدوس الأشهر المعتمدة/المقفلة.
        await MonthAttendanceStore.BuildMonthAsync(dbContext, run.Year, run.Month);
        var months = (await MonthAttendanceStore.ListAsync(dbContext,
            runCompanyId is > 0 ? CompanyScope.ForCompanies(new[] { runCompanyId.Value }) : CompanyScope.Unrestricted(),
            run.Year, run.Month))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        // المخالفات مع **قاعدة جزائها**: الوعاء والمقام صارا بياناتٍ على القاعدة لا
        // ثابتين بالكود (`Basic ÷ 30`). صفٌّ بلا قاعدة أو بقاعدةٍ بلا وعاء يرجع
        // للافتراضين المعلنين — أي **نفس رقم اليوم بالضبط**.
        var penalties = (await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT v.EmployeeId,
       ISNULL(v.DeductionAmount,0) AS DeductionAmount,
       ISNULL(v.FinancialImpactType,N'None') AS FinancialImpactType,
       ISNULL(v.FinancialImpactValue,0) AS FinancialImpactValue,
       ISNULL(r.BasePoolJson, N'') AS BasePoolJson,
       ISNULL(r.WorkDaysBasis, N'Fixed') AS WorkDaysBasis,
       ISNULL(r.WorkDaysFixed, 30) AS WorkDaysFixed,
       ISNULL(r.ExcludeHolidays, N'None') AS ExcludeHolidays,
       ISNULL(s.Name, N'') AS SalaryItemName
FROM EmployeeViolationCases v
LEFT JOIN DisciplinaryPenaltyRules r ON r.Id = v.PenaltyRuleId
LEFT JOIN SalaryItems s ON s.Id = r.SalaryItemId
WHERE ISNULL(v.IsDeleted,0)=0 AND v.EventDate >= @From AND v.EventDate <= @To;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", periodStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", periodEnd.ToDateTime(TimeOnly.MaxValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Direct = reader["DeductionAmount"] is decimal da ? da : 0,
                Type = HrmsDatabase.GetString(reader, "FinancialImpactType"),
                Value = reader["FinancialImpactValue"] is decimal v ? v : 0,
                Pool = HrmsDatabase.GetString(reader, "BasePoolJson"),
                Basis = HrmsDatabase.GetString(reader, "WorkDaysBasis"),
                BasisDays = HrmsDatabase.GetInt(reader, "WorkDaysFixed"),
                Exclude = HrmsDatabase.GetString(reader, "ExcludeHolidays"),
                ItemName = HrmsDatabase.GetString(reader, "SalaryItemName")
            }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // أيام الفترة الفعلية — مقامٌ للخيار «أيام فترة الراتب».
        var daysInPeriod = periodEnd.DayNumber - periodStart.DayNumber + 1;

        // سقف اقتطاع المخالفات الشهري من تهيئة اللائحة (صفر = بلا سقف).
        var maxDeductionPercent = decimal.TryParse(
            await HrmsDatabase.ScalarAsync<string>(
                dbContext,
                "SELECT TOP 1 [Value] FROM DisciplinarySettings WHERE [Key] = N'MaxDeductionPercentOfSalary';",
                command => { }),
            out var parsedCap) ? parsedCap : 0m;

        // حركات الدخل/الاقتطاع للفترة (شاشة «الحركات») — بنود إضافية/خصم بالقسيمة
        var income = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Income))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var deductionTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Deduction))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var overtimeTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Overtime))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var salaryDaysTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.SalaryDays))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var leaveEncashTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.LeaveEncashment))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // عناصر الراتب ذات الصيغة (غير النظامية النشطة) — تُقيَّم لكل موظف بمحرك الصيغ
        // وتُضاف بنوداً للقسيمة (استحقاق يدخل الإجمالي/الوعاء الخاضع، أو اقتطاع). عناصر
        // النظام (الأساسي/الضريبة/الضمان) مستثناة — يعالجها المحرك مباشرةً.
        var formulaItems = (await SalaryItemStore.ListAsync(dbContext))
            .Where(x => x.IsActive && !x.IsSystem && x.ValueKind == "Formula" && !string.IsNullOrWhiteSpace(x.Formula))
            .OrderBy(x => x.SortOrder).ToList();

        // القيم الثابتة المسمّاة تُقرأ مرّة للدفعة كلّها لا لكل موظف.
        var salaryConstants = formulaItems.Count > 0
            ? await SalaryConstantStore.ActiveMapAsync(dbContext)
            : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // --- بناء السطور ---
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE c FROM PayrollRunLineComponents c INNER JOIN PayrollRunLines l ON l.Id = c.LineId WHERE l.RunId = @RunId; DELETE FROM PayrollRunLines WHERE RunId = @RunId;",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId));

        int count = 0, skippedStopped = 0, skippedNoSalary = 0, skippedNoAttendance = 0, paidWithoutAttendance = 0;
        decimal totalGross = 0, totalNet = 0, totalTax = 0, totalGosiCo = 0;

        // سجلّ المستبعَدين لهذه الدفعة — يُجمع بالذاكرة ويُكتب دفعةً واحدة داخل
        // المعاملة نفسها، فلا يبقى سجلٌّ لدفعةٍ فشل احتسابها.
        var exclusions = new List<(int EmployeeId, string Code, string Reason)>();

        foreach (var emp in candidates)
        {
            financial.TryGetValue(emp.Id, out var fin);
            if (fin?.Stop == true)
            {
                // مستبعَد من الاحتساب بالملف المالي
                skippedStopped++;
                exclusions.Add((emp.Id, "Stopped", "موقوف الاحتساب بالملف المالي — ارفع الإيقاف من الملف المالي للموظف."));
                continue;
            }
            var basic = fin?.Basic ?? 0;
            if (basic <= 0 && !allowances.ContainsKey(emp.Id) && !income.ContainsKey(emp.Id)
                && !overtimeTx.ContainsKey(emp.Id) && !salaryDaysTx.ContainsKey(emp.Id)
                && !leaveEncashTx.ContainsKey(emp.Id))
            {
                // لا راتب ولا علاوات ولا حركات دخل/عمل إضافي/أيام/بدل إجازة.
                // يُعدّ لا يُبتلع: «لماذا قسيمة واحدة فقط؟» جوابه هنا عادةً.
                skippedNoSalary++;
                exclusions.Add((emp.Id, "NoSalary", "بلا راتب أساسي ولا علاوات ولا حركات مالية بالفترة — أدخل الراتب الأساسي بالملف المالي."));
                continue;
            }

            // تنسيب الأساسي حسب الحضور — بسياسة الربط المختارة لا بقاعدة مثبّتة.
            // كان غياب صفّ الاعتماد يعني معامل 1 بصمت (قسيمة «0 / 0» براتب كامل).
            months.TryGetValue(emp.Id, out var month);
            var workDays = month?.WorkDays ?? 0;
            var absentDays = month?.AbsentDays ?? 0;
            var unpaidLeaveDays = month?.UnpaidLeaveDays ?? 0;

            var link = AttendanceSalaryLink.Evaluate(
                linkPolicy, workDays, month?.PresentDays ?? 0, absentDays, month?.WorkedHours ?? 0m);
            if (!link.Include)
            {
                skippedNoAttendance++;
                exclusions.Add((emp.Id,
                    "NoAttendance",
                    $"لا يستوفي شرط ربط الحضور بالمسير ({AttendanceSalaryLink.ModeLabel(linkMode)}) — "
                    + $"أيام دوام {workDays} · حضور {month?.PresentDays ?? 0} · غياب {absentDays}. شغّل «تحديث الحضور» للفترة."));
                continue;
            }
            if (!AttendanceSalaryLink.HasAttendanceData(workDays)) paidWithoutAttendance++;

            var factor = link.Factor;
            var proratedBasic = Math.Round(basic * factor, 2);

            var dailyRate = basic > 0 ? Math.Round(basic / 30m, 4) : 0;
            var hourlyRate = dailyRate > 0 ? Math.Round(dailyRate / 8m, 4) : 0;

            var comps = new List<Component>();
            if (proratedBasic != 0)
                comps.Add(new Component { ItemName = "الراتب الأساسي", Amount = proratedBasic, IsAddition = true, Kind = "Basic" });

            decimal allowancesTotal = 0;
            if (allowances.TryGetValue(emp.Id, out var empAllow))
            {
                foreach (var al in empAllow)
                {
                    var active = (al.From == null || al.From <= periodEnd)
                        && (al.To == null || !al.EndAfter || al.To >= periodStart);
                    if (!active || al.Amount == 0) continue;
                    allowancesTotal += al.Amount;
                    comps.Add(new Component { ItemName = al.ItemName, Amount = al.Amount, IsAddition = true, Kind = "Allowance" });
                }
            }

            // حركات الدخل (مكافآت/حوافز/بدلات لمرة) — بنود إضافية، بعضها غير خاضع للضريبة
            decimal incomeTotal = 0, taxableIncome = 0;
            if (income.TryGetValue(emp.Id, out var empIncome))
            {
                foreach (var t in empIncome)
                {
                    if (t.Amount == 0) continue;
                    incomeTotal += t.Amount;
                    if (t.Taxable) taxableIncome += t.Amount;
                    comps.Add(new Component { ItemName = t.ItemName, Amount = t.Amount, IsAddition = true, Kind = "Income" });
                }
            }

            // العمل الإضافي (شاشة «العمل الإضافي») — ساعات × الأجر الساعي × معامل البدل.
            // إن لم تُحدَّد ساعات (إدخال مبلغ يدوي) يُستخدم المبلغ المخزّن كما هو.
            decimal overtimeTotal = 0, taxableOvertime = 0;
            if (overtimeTx.TryGetValue(emp.Id, out var empOt))
            {
                foreach (var t in empOt)
                {
                    var amt = t.Hours is > 0
                        ? Math.Round(t.Hours.Value * hourlyRate * (t.RateFactor ?? PayrollTransactionStore.DefaultRateFactor), 2)
                        : t.Amount;
                    if (amt == 0) continue;
                    overtimeTotal += amt;
                    if (t.Taxable) taxableOvertime += amt;
                    var label = t.Hours is > 0
                        ? $"{t.ItemName} ({t.Hours:0.##}س × {t.RateFactor ?? PayrollTransactionStore.DefaultRateFactor:0.##})"
                        : t.ItemName;
                    comps.Add(new Component { ItemName = label, Amount = amt, IsAddition = true, Kind = "Overtime" });
                }
            }

            // تعديل أيام الراتب (شاشة «تعديل أيام الراتب») — أيام موقّعة × الأجر اليومي.
            // موجب = إضافة أيام (استحقاق يدخل الإجمالي والوعاء الخاضع)، سالب = خصم أيام
            // (استقطاع بعد الإجمالي كنمط حركات الاقتطاع). إن لم تُحدَّد أيام يُستخدم المبلغ
            // المخزّن موقّعاً. الأجر اليومي = الأساسي ÷ 30.
            decimal salaryDaysAdd = 0, salaryDaysDeduct = 0;
            if (salaryDaysTx.TryGetValue(emp.Id, out var empDays))
            {
                foreach (var t in empDays)
                {
                    var signed = t.Days.HasValue && t.Days.Value != 0
                        ? Math.Round(t.Days.Value * dailyRate, 2)
                        : t.Amount;
                    if (signed == 0) continue;
                    var abs = Math.Abs(signed);
                    var label = t.Days.HasValue && t.Days.Value != 0
                        ? $"{t.ItemName} ({t.Days:+0.##;-0.##} يوم)"
                        : t.ItemName;
                    if (signed > 0) salaryDaysAdd += abs; else salaryDaysDeduct += abs;
                    comps.Add(new Component { ItemName = label, Amount = abs, IsAddition = signed > 0, Kind = "SalaryDays" });
                }
            }

            // بدل الإجازة (شاشة «بدل إجازة») — صرف أيام رصيد إجازة نقداً: أيام × الأجر
            // اليومي (أساسي÷30) كاستحقاق يدخل الإجمالي والوعاء الخاضع. Amount يدوي بديل.
            decimal leaveEncashTotal = 0, taxableLeaveEncash = 0;
            if (leaveEncashTx.TryGetValue(emp.Id, out var empEnc))
            {
                foreach (var t in empEnc)
                {
                    var amt = t.Days is > 0 ? Math.Round(t.Days.Value * dailyRate, 2) : t.Amount;
                    if (amt == 0) continue;
                    leaveEncashTotal += amt;
                    if (t.Taxable) taxableLeaveEncash += amt;
                    var label = t.Days is > 0 ? $"{t.ItemName} ({t.Days:0.##} يوم)" : t.ItemName;
                    comps.Add(new Component { ItemName = label, Amount = amt, IsAddition = true, Kind = "LeaveEncashment" });
                }
            }

            // محرك الصيغ: كل عنصر معادلة يُقيَّم بمتغيّرات الموظف. الاستحقاق يدخل
            // الإجمالي (والوعاء الخاضع إن كان خاضعاً)، والاقتطاع بعد الإجمالي. النسبي
            // يُضرب بمعامل الحضور. الصيغة المعطوبة تُتخطّى بلا إسقاط المسير.
            decimal formulaAddTotal = 0, formulaTaxableAdd = 0, formulaDeductTotal = 0;
            if (formulaItems.Count > 0)
            {
                // ⚠️ كان القاموس يُبنى هنا بيده بسبعة مفاتيح، **واثنان منها صفران
                // مثبّتان** (`Hours`/`Days`) رغم توفّر قيمتيهما الفعليتين بالنطاق
                // نفسه. فصيغةٌ كـ`Basic / 30 * Days` تُنتج صفراً فيُتخطّى بندها،
                // و`Basic / Days` تفشل بقسمة على صفر فيُتخطّى **بصمت**. والأسوأ أن
                // مختبر الصيغ كان يمرّر `Days=30, Hours=8` — فالرقم الذي يراه
                // المستخدم بالتجربة ليس الرقم الذي يدخل القسيمة.
                // البناء الآن من مصدر واحد: PayrollFormulaVariables.
                var formulaVars = PayrollFormulaVariables.Build(new PayrollFormulaVariables.Context
                {
                    Basic = basic,
                    ProratedBasic = proratedBasic,
                    Allowances = allowancesTotal,
                    Days = workDays,
                    PresentDays = month?.PresentDays ?? 0,
                    AbsentDays = absentDays,
                    UnpaidLeaveDays = unpaidLeaveDays,
                    DaysInPeriod = daysInPeriod,
                    Hours = month?.WorkedHours ?? 0m,
                    DailyRate = dailyRate,
                    HourlyRate = hourlyRate,
                    Factor = factor,
                    IncomeTx = incomeTotal,
                    OvertimeTx = overtimeTotal,
                    SalaryDaysTx = salaryDaysAdd - salaryDaysDeduct,
                    LeaveEncashTx = leaveEncashTotal,
                    DeductionTx = deductionTx.TryGetValue(emp.Id, out var empDedPreview)
                        ? empDedPreview.Sum(t => t.Amount)
                        : 0m
                },
                salaryConstants);
                foreach (var item in formulaItems)
                {
                    if (!SalaryFormulaEvaluator.TryEvaluate(item.Formula, formulaVars, out var raw, out _)) continue;
                    var value = Math.Round(item.Prorated ? raw * factor : raw, 2);
                    if (value == 0) continue;
                    if (item.IsAddition)
                    {
                        formulaAddTotal += value;
                        if (item.Taxable) formulaTaxableAdd += value;
                        comps.Add(new Component { ItemName = item.Name, Amount = value, IsAddition = true, Kind = "Formula" });
                    }
                    else
                    {
                        formulaDeductTotal += value;
                        comps.Add(new Component { ItemName = item.Name, Amount = value, IsAddition = false, Kind = "Formula" });
                    }
                }
            }

            var gross = Math.Round(proratedBasic + allowancesTotal + incomeTotal + overtimeTotal + salaryDaysAdd + leaveEncashTotal + formulaAddTotal, 2);

            // مكوّنات القسيمة تُسلَّم لمركِّب الأوعية بدل جمعها هنا: العضوية صارت
            // بيانات (SalaryBaseComposer.Default*Members) تمهيداً لتحريرها من الواجهة.
            // الافتراضيان يعيدان نفس رقمَي ما قبل الفصل تماماً — مثبَّت باختبارات.
            var baseAmounts = new SalaryBaseComposer.Amounts
            {
                Basic = proratedBasic,
                Allowances = allowancesTotal,
                TaxableIncome = taxableIncome,
                TaxableOvertime = taxableOvertime,
                SalaryDays = salaryDaysAdd,
                LeaveEncashment = taxableLeaveEncash,
                FormulaAdd = formulaTaxableAdd,
                Gross = gross
            };

            // حسم ملفَّي الضريبة والضمان لهذا الموظف: إسناده الصريح ⟵ فملفٌ تنطبق
            // شروطه ⟵ فالملف النشط. ووعاء الاحتساب يتبع الملف الفائز لا ملفاً ثابتاً.
            var facts = factsByEmployee.TryGetValue(emp.Id, out var empFacts) ? empFacts : noFacts;

            var taxChoice = PayrollProfileResolver.Resolve(fin?.TaxProfileId, taxCandidates, facts);
            var gosiChoice = PayrollProfileResolver.Resolve(fin?.GosiProfileId, gosiCandidates, facts);

            var taxProfile = taxChoice.ProfileId is { } taxId ? taxById.GetValueOrDefault(taxId) : null;
            var gosiProfile = gosiChoice.ProfileId is { } gosiId ? gosiById.GetValueOrDefault(gosiId) : null;

            var taxMembers = SalaryBaseStore.Resolve(
                baseMembers, SalaryBaseComposer.TaxBaseKey, taxProfile?.Id ?? 0);
            var gosiMembers = SalaryBaseStore.Resolve(
                baseMembers, SalaryBaseComposer.GosiBaseKey, gosiProfile?.Id ?? 0);

            var taxableBase = SalaryBaseComposer.Compose(baseAmounts, taxMembers);
            var tax = PayrollConfigStore.ComputeTax(taxableBase, taxProfile);
            var (gosiEmp, gosiCo) = PayrollConfigStore.ComputeGosi(
                SalaryBaseComposer.Compose(baseAmounts, gosiMembers), gosiProfile);

            // خصومات المخالفات: مباشر بالدينار + أيام×يومي + ساعات×ساعي.
            //
            // ⚠️ **اليوميّ هنا يخصّ كل مخالفة على حدة** لا الموظف: وعاء الخصم ومقام
            // أيام العمل صارا حقلين على قاعدة الجزاء، فقد تُخصم مخالفةٌ من الأساسي
            // وحده وأخرى من الأساسي زائد بدل السكن. الفراغ يعطي `Basic ÷ 30` —
            // سلوك ما قبل الميزة بالضبط.
            //
            // وأيام غير العمل تُطرح من المقام حين يطلبها الجزاء: أيام الفترة ناقص
            // أيام عمل الموظف بالاعتماد الشهري. لا اعتماد ⟹ لا طرح.
            var nonWorkDays = month is { WorkDays: > 0 }
                ? Math.Max(0, daysInPeriod - month.WorkDays)
                : 0;

            decimal penaltyTotal = 0;
            if (penalties.TryGetValue(emp.Id, out var empPen))
            {
                var penaltyByItem = new Dictionary<string, decimal>(StringComparer.Ordinal);

                foreach (var p in empPen)
                {
                    var pool = PenaltyBasePool.Amount(baseAmounts, PenaltyBasePool.Parse(p.Pool));
                    var divisor = WorkDaysBasis.Divisor(
                        p.Basis, p.BasisDays, daysInPeriod,
                        companyHolidays: 0, restDays: nonWorkDays, excludeMode: p.Exclude);

                    var penaltyDaily = WorkDaysBasis.DailyRate(pool, divisor);
                    var penaltyHourly = WorkDaysBasis.HourlyRate(penaltyDaily);

                    var amt = p.Direct;
                    if (p.Type == "Days") amt += Math.Round(p.Value * penaltyDaily, 2);
                    else if (p.Type == "Hours") amt += Math.Round(p.Value * penaltyHourly, 2);
                    else if (p.Type == "Amount") amt += p.Value;

                    if (amt == 0) continue;

                    // وجهة الترحيل: بند الاقتطاع المسمّى إن وُجد، وإلا السطر العام.
                    // بدونها كان الخصم يظهر رقماً بلا حسابٍ يُرحَّل إليه.
                    var itemName = string.IsNullOrWhiteSpace(p.ItemName) ? "خصومات المخالفات" : p.ItemName;
                    penaltyByItem[itemName] = penaltyByItem.GetValueOrDefault(itemName) + amt;
                }

                // 🔒 سقف الاقتطاع الشهري (لائحة الجزاءات مادة 6 و9: 20% من الراتب).
                //
                // يُطبَّق على **المجموع** لا على كل مخالفة: خمس مخالفات بشهرٍ واحد
                // كانت ستبتلع الراتب كلّه، وحدٌّ على المفردة وحدها يلتفّ عليه التكرار.
                // والتخفيض يُوزَّع تناسبياً على البنود كي تبقى الوجهات المحاسبية صحيحة.
                var rawPenalty = penaltyByItem.Values.Where(v => v > 0).Sum();
                var cappedPenalty = PenaltyBasePool.CapMonthlyDeduction(rawPenalty, gross, maxDeductionPercent);
                var capFactor = rawPenalty > 0 ? cappedPenalty / rawPenalty : 1m;

                foreach (var entry in penaltyByItem.Where(e => e.Value > 0))
                {
                    var amount = decimal.Round(entry.Value * capFactor, 2);
                    if (amount <= 0) continue;

                    penaltyTotal += amount;
                    comps.Add(new Component
                    {
                        ItemName = entry.Key,
                        Amount = amount,
                        IsAddition = false,
                        Kind = "Penalty"
                    });
                }

                if (cappedPenalty < rawPenalty)
                {
                    comps.Add(new Component
                    {
                        ItemName = $"— حُدّ خصم المخالفات بسقف {maxDeductionPercent:0.##}% من الراتب",
                        Amount = 0,
                        IsAddition = false,
                        Kind = "Penalty"
                    });
                }
            }

            if (tax > 0) comps.Add(new Component { ItemName = "ضريبة الدخل", Amount = tax, IsAddition = false, Kind = "Tax" });
            if (gosiEmp > 0) comps.Add(new Component { ItemName = "الضمان الاجتماعي (حصة الموظف)", Amount = gosiEmp, IsAddition = false, Kind = "Gosi" });

            // حركات الاقتطاع (خصومات مُدخلة يدوياً بشاشة الحركات)
            decimal deductionTxTotal = 0;
            if (deductionTx.TryGetValue(emp.Id, out var empDed))
            {
                foreach (var t in empDed)
                {
                    if (t.Amount == 0) continue;
                    deductionTxTotal += t.Amount;
                    comps.Add(new Component { ItemName = t.ItemName, Amount = t.Amount, IsAddition = false, Kind = "Deduction" });
                }
            }

            // الإجازة غير المدفوعة (ربط الإجازات بالمسير): يوم الإجازة غير المدفوعة يُعدّ
            // يوم عمل بالحضور (فيُدفع ضمن الأساسي) فنخصمه هنا يوماً×الأجر اليومي (أساسي÷30)
            // كخصم post-gross — نفس نمط تعديل الأيام والمخالفات. الوعاء الخاضع لا يتأثر.
            decimal unpaidLeaveDeduct = 0;
            if (unpaidLeaveDays > 0 && dailyRate > 0)
            {
                unpaidLeaveDeduct = Math.Round(unpaidLeaveDays * dailyRate, 2);
                comps.Add(new Component
                {
                    ItemName = $"إجازة بدون راتب ({unpaidLeaveDays} يوم)",
                    Amount = unpaidLeaveDeduct,
                    IsAddition = false,
                    Kind = "Leave"
                });
            }

            var otherDeductions = penaltyTotal + deductionTxTotal + salaryDaysDeduct + unpaidLeaveDeduct + formulaDeductTotal;
            var net = Math.Round(gross - tax - gosiEmp - otherDeductions, 2);

            var lineId = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO PayrollRunLines
  (RunId, EmployeeId, BasicSalary, TotalAllowances, GrossSalary, TaxAmount, GosiEmployee, GosiCompany, OtherDeductions, NetSalary, WorkDays, AbsentDays)
VALUES
  (@RunId, @Emp, @Basic, @Allow, @Gross, @Tax, @GosiEmp, @GosiCo, @Other, @Net, @WorkDays, @AbsentDays);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RunId", runId);
                    HrmsDatabase.AddParameter(command, "@Emp", emp.Id);
                    HrmsDatabase.AddParameter(command, "@Basic", proratedBasic);
                    HrmsDatabase.AddParameter(command, "@Allow", allowancesTotal);
                    HrmsDatabase.AddParameter(command, "@Gross", gross);
                    HrmsDatabase.AddParameter(command, "@Tax", tax);
                    HrmsDatabase.AddParameter(command, "@GosiEmp", gosiEmp);
                    HrmsDatabase.AddParameter(command, "@GosiCo", gosiCo);
                    HrmsDatabase.AddParameter(command, "@Other", otherDeductions);
                    HrmsDatabase.AddParameter(command, "@Net", net);
                    HrmsDatabase.AddParameter(command, "@WorkDays", workDays);
                    HrmsDatabase.AddParameter(command, "@AbsentDays", absentDays);
                });

            foreach (var c in comps)
            {
                var current = c;
                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "INSERT INTO PayrollRunLineComponents (LineId, ItemName, Amount, IsAddition, Kind) VALUES (@Line, @Name, @Amount, @Add, @Kind);",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Line", lineId);
                        HrmsDatabase.AddParameter(command, "@Name", current.ItemName);
                        HrmsDatabase.AddParameter(command, "@Amount", current.Amount);
                        HrmsDatabase.AddParameter(command, "@Add", current.IsAddition ? 1 : 0);
                        HrmsDatabase.AddParameter(command, "@Kind", current.Kind);
                    });
            }

            count++;
            totalGross += gross; totalNet += net; totalTax += tax; totalGosiCo += gosiCo;
        }

        // سجلّ المستبعَدين يُستبدل كاملاً مع كل احتساب (idempotent كسطور الدفعة):
        // إعادة الاحتساب بعد إصلاح البيانات يجب أن تُخلي السجلّ من الأسماء التي عولجت.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PayrollRunExclusions WHERE RunId = @RunId;",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId));

        foreach (var (employeeId, code, reason) in exclusions)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO PayrollRunExclusions (RunId, EmployeeId, ReasonCode, Reason) VALUES (@RunId, @Emp, @Code, @Reason);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RunId", runId);
                    HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                    HrmsDatabase.AddParameter(command, "@Code", code);
                    HrmsDatabase.AddParameter(command, "@Reason", reason);
                });
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE PayrollRuns
SET Status = N'Calculated', EmployeeCount = @Count, TotalGross = @Gross, TotalNet = @Net,
    TotalTax = @Tax, TotalGosiCompany = @GosiCo, CalculatedBy = @By, CalculatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", runId);
                HrmsDatabase.AddParameter(command, "@Count", count);
                HrmsDatabase.AddParameter(command, "@Gross", totalGross);
                HrmsDatabase.AddParameter(command, "@Net", totalNet);
                HrmsDatabase.AddParameter(command, "@Tax", totalTax);
                HrmsDatabase.AddParameter(command, "@GosiCo", totalGosiCo);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });

        await transaction.CommitAsync();

        // تفصيل المتخطَّين يُعرض دائماً: تشغيلٌ يعيد قسيمة واحدة من ألف موظف كان
        // يبدو عطلاً بالمحرك، وسببه غالباً بيانات (بلا راتب أساسي/موقوف الاحتساب).
        var skips = new List<string>();
        if (skippedNoSalary > 0) skips.Add($"{skippedNoSalary} بلا راتب أساسي أو حركات");
        if (skippedStopped > 0) skips.Add($"{skippedStopped} موقوف الاحتساب بالملف المالي");
        if (skippedNoAttendance > 0) skips.Add($"{skippedNoAttendance} بلا بيانات حضور ({AttendanceSalaryLink.ModeLabel(linkMode)})");
        if (outsideScope > 0) skips.Add($"{outsideScope} من النطاق خارج قائمة النشطين");
        var skipText = skips.Count > 0 ? $" · تُخطّي: {string.Join(" · ", skips)}" : string.Empty;

        // الدفع بلا بيانات حضور يُعلَن دائماً لا يُبتلع — هذا جوهر شكوى «0 / 0 براتب كامل».
        if (paidWithoutAttendance > 0)
            skipText += $" · ⚠ {paidWithoutAttendance} بلا بيانات حضور دُفعوا الأساسي كاملاً";

        var scopeText = scope.Count > 0 ? $"نطاق {scope.Count} موظفاً" : "كل النشطين";
        return (true, count == 0
            ? $"لم يُحتسب أحد ({scopeText}){skipText} — تأكد من الرواتب الأساسية بالملف المالي."
            : $"احتُسب {count} موظفاً من {scopeText} — إجمالي {totalGross:0.##}، صافي {totalNet:0.##}{skipText}.");
    }

    /// <summary>صفّ بسجلّ المستبعَدين — الموظف وسبب استبعاده من هذه الدفعة.</summary>
    public sealed class Exclusion
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        public string ReasonLabel => ReasonCode switch
        {
            "Stopped" => "موقوف الاحتساب",
            "NoSalary" => "بلا راتب أو حركات",
            "NoAttendance" => "لا يستوفي ربط الحضور",
            _ => "استبعاد"
        };
    }

    /// <summary>
    /// المستبعَدون من دفعةٍ بأسمائهم وأسبابهم (نظير تبويب «الرواتب المستثناة» بكيان).
    /// يُعرض بتبويب مستقلّ بشاشة الدفعة — العدّاد المجمّع وحده لا يجيب «لماذا فلان؟».
    /// </summary>
    public static async Task<List<Exclusion>> ListExclusionsAsync(ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT x.EmployeeId, x.ReasonCode, x.Reason,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName
FROM PayrollRunExclusions x
INNER JOIN Employees e ON e.Id = x.EmployeeId
WHERE x.RunId = @RunId
ORDER BY x.ReasonCode, e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new Exclusion
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                ReasonCode = HrmsDatabase.GetString(reader, "ReasonCode"),
                Reason = HrmsDatabase.GetString(reader, "Reason")
            });
    }

    public static async Task<List<PayrollLine>> ListLinesAsync(ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        var lines = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT l.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(e.Position, N'') AS Position, ISNULL(d.Name, N'') AS DepartmentName
FROM PayrollRunLines l
INNER JOIN Employees e ON e.Id = l.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
WHERE l.RunId = @RunId
ORDER BY e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new PayrollLine
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RunId = HrmsDatabase.GetInt(reader, "RunId"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                BasicSalary = reader["BasicSalary"] is decimal b ? b : 0,
                TotalAllowances = reader["TotalAllowances"] is decimal a ? a : 0,
                GrossSalary = reader["GrossSalary"] is decimal g ? g : 0,
                TaxAmount = reader["TaxAmount"] is decimal t ? t : 0,
                GosiEmployee = reader["GosiEmployee"] is decimal ge ? ge : 0,
                GosiCompany = reader["GosiCompany"] is decimal gc ? gc : 0,
                OtherDeductions = reader["OtherDeductions"] is decimal o ? o : 0,
                NetSalary = reader["NetSalary"] is decimal n ? n : 0,
                WorkDays = HrmsDatabase.GetInt(reader, "WorkDays"),
                AbsentDays = HrmsDatabase.GetInt(reader, "AbsentDays")
            });

        if (lines.Count > 0)
        {
            var comps = await HrmsDatabase.QueryAsync(
                dbContext,
                """
SELECT c.LineId, c.ItemName, c.Amount, c.IsAddition, c.Kind
FROM PayrollRunLineComponents c
INNER JOIN PayrollRunLines l ON l.Id = c.LineId
WHERE l.RunId = @RunId
ORDER BY c.IsAddition DESC, c.Id;
""",
                command => HrmsDatabase.AddParameter(command, "@RunId", runId),
                reader => new
                {
                    LineId = HrmsDatabase.GetInt(reader, "LineId"),
                    Comp = new Component
                    {
                        ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                        Amount = reader["Amount"] is decimal a ? a : 0,
                        IsAddition = HrmsDatabase.GetBool(reader, "IsAddition"),
                        Kind = HrmsDatabase.GetString(reader, "Kind")
                    }
                });
            var byLine = comps.GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.Select(x => x.Comp).ToList());
            foreach (var l in lines)
                l.Components = byLine.TryGetValue(l.Id, out var list) ? list : new();
        }
        return lines;
    }

    /// <summary>صف ملف البنك: بيانات الدفع للموظف + صافي راتبه بالدفعة.</summary>
    public sealed class BankFileRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string BankBranch { get; set; } = string.Empty;
        public string Iban { get; set; } = string.Empty;
        public string CardNo { get; set; } = string.Empty;
        public decimal NetSalary { get; set; }

        /// <summary>لا آيبان ولا رقم بطاقة ⟹ الصف لا يُحوَّل، ويُعلَّم ليصححه المستخدم.</summary>
        public bool IsPayable => !string.IsNullOrWhiteSpace(Iban) || !string.IsNullOrWhiteSpace(CardNo);
    }

    /// <summary>
    /// صفوف ملف البنك لدفعة: سطور المسير + بيانات الدفع من الملف المالي للموظف.
    /// الصفوف بلا آيبان/بطاقة تُرجَع أيضاً (معلَّمة) بدل أن تختفي بصمت.
    /// </summary>
    public static async Task<List<BankFileRow>> BankFileRowsAsync(
        ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);

        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(f.PaymentMethod, N'') AS PaymentMethod, ISNULL(f.BankName, N'') AS BankName,
       ISNULL(f.BankBranch, N'') AS BankBranch, ISNULL(f.Iban, N'') AS Iban,
       ISNULL(f.CardNo, N'') AS CardNo, l.NetSalary
FROM PayrollRunLines l
INNER JOIN Employees e ON e.Id = l.EmployeeId
OUTER APPLY (
    SELECT TOP 1 * FROM EmployeeFinancialInfos fi
    WHERE fi.EmployeeId = l.EmployeeId AND ISNULL(fi.IsDeleted, 0) = 0
    ORDER BY fi.Id DESC
) f
WHERE l.RunId = @RunId
ORDER BY e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new BankFileRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                PaymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                BankName = HrmsDatabase.GetString(reader, "BankName"),
                BankBranch = HrmsDatabase.GetString(reader, "BankBranch"),
                Iban = HrmsDatabase.GetString(reader, "Iban"),
                CardNo = HrmsDatabase.GetString(reader, "CardNo"),
                NetSalary = reader["NetSalary"] is decimal n ? n : 0
            });
    }

    public static async Task<PayrollLine?> GetLineAsync(ApplicationDbContext dbContext, int runId, int employeeId) =>
        (await ListLinesAsync(dbContext, runId)).FirstOrDefault(x => x.EmployeeId == employeeId);

    // ---------------- دورة الحياة ----------------
    public static async Task<(bool, string)> LockAsync(ApplicationDbContext dbContext, int runId)
    {
        var run = await GetRunAsync(dbContext, runId);
        var res = await TransitionAsync(dbContext, runId, from: "Calculated", to: "Locked", "LockedAt", "قُفلت الدفعة.");
        // قفل حركات الدفعة (لكل حركة) — الحركات الجديدة بعدها تبقى غير مقفلة
        if (res.Item1 && run != null)
            await PayrollTransactionStore.LockForRunAsync(dbContext, runId, run.Year, run.Month);
        return res;
    }

    public static Task<(bool, string)> IssueAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Locked", to: "Issued", "IssuedAt", "اعتُمدت للصرف.");

    public static Task<(bool, string)> SendPayslipsAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Issued", to: "PayslipSent", "PayslipSentAt", "أُرسلت القسائم.");

    public static Task<(bool, string)> ReopenAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Calculated", to: "Draft", null, "أُعيدت للمسودة.");

    public static async Task<(bool, string)> DeleteRunAsync(ApplicationDbContext dbContext, int runId)
    {
        var run = await GetRunAsync(dbContext, runId);
        if (run == null) return (false, "غير موجودة.");
        if (run.Status is not ("Draft" or "Calculated")) return (false, "لا تُحذف دفعة مقفلة/معتمدة.");
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE c FROM PayrollRunLineComponents c INNER JOIN PayrollRunLines l ON l.Id = c.LineId WHERE l.RunId = @Id; DELETE FROM PayrollRunLines WHERE RunId = @Id; DELETE FROM PayrollRunScopeMembers WHERE RunId = @Id; DELETE FROM PayrollRuns WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", runId));
        return (true, "حُذفت الدفعة.");
    }

    /// <summary>
    /// تعديل نطاق دفعة قبل قفلها. النطاق جزء من مدخلات الاحتساب فلا يُمسّ بعد
    /// القفل: تغييرُه على دفعة مقفلة يجعل سطورها لا تطابق نطاقها المعلن.
    /// قائمة فارغة ⟹ عودة لـ«كل الموظفين».
    /// </summary>
    public static async Task<(bool Ok, string Message)> SetScopeAsync(
        ApplicationDbContext dbContext, int runId, string? scopeMode, IEnumerable<int> employeeIds)
    {
        var run = await GetRunAsync(dbContext, runId);
        if (run == null) return (false, "الدفعة غير موجودة.");
        if (run.Status is not ("Draft" or "Calculated"))
            return (false, "لا يُعدَّل نطاق دفعة مقفلة/معتمدة.");

        var ids = employeeIds.Distinct().ToList();
        var mode = ids.Count == 0 ? PayrollRunScope.ModeAll : PayrollRunScope.NormalizeMode(scopeMode);

        await PayrollRunScopeStore.ReplaceAsync(dbContext, runId, ids);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE PayrollRuns SET ScopeMode = @Scope WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", runId);
                HrmsDatabase.AddParameter(command, "@Scope", mode);
            });

        return (true, run.Status == "Calculated"
            ? $"حُدِّث النطاق: {PayrollRunScope.Describe(mode, ids.Count)} — أعد «الاحتساب» ليأخذ أثره."
            : $"حُدِّث النطاق: {PayrollRunScope.Describe(mode, ids.Count)}.");
    }

    private static async Task<(bool, string)> TransitionAsync(
        ApplicationDbContext dbContext, int runId, string from, string to, string? stampColumn, string okMessage)
    {
        await EnsureAsync(dbContext);
        var extra = stampColumn == null ? "" : $", {stampColumn} = SYSUTCDATETIME()";
        var affected = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"UPDATE PayrollRuns SET Status = @To{extra} WHERE Id = @Id AND Status = @From; SELECT @@ROWCOUNT;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", runId);
                HrmsDatabase.AddParameter(command, "@To", to);
                HrmsDatabase.AddParameter(command, "@From", from);
            });
        return affected > 0 ? (true, okMessage) : (false, "الحالة الحالية لا تسمح بهذا الانتقال.");
    }

    private static PayrollRun ReadRun(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        BatchNo = HrmsDatabase.GetString(reader, "BatchNo"),
        Year = HrmsDatabase.GetInt(reader, "Year"),
        Month = HrmsDatabase.GetInt(reader, "Month"),
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } s ? s : "Draft",
        EmployeeCount = HrmsDatabase.GetInt(reader, "EmployeeCount"),
        TotalGross = reader["TotalGross"] is decimal g ? g : 0,
        TotalNet = reader["TotalNet"] is decimal n ? n : 0,
        TotalTax = reader["TotalTax"] is decimal t ? t : 0,
        TotalGosiCompany = reader["TotalGosiCompany"] is decimal gc ? gc : 0,
        CalculatedBy = HrmsDatabase.GetString(reader, "CalculatedBy") is { Length: > 0 } by ? by : null,
        CalculatedAt = HrmsDatabase.GetDateTime(reader, "CalculatedAt"),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default,
        ScopeMode = PayrollRunScope.NormalizeMode(HrmsDatabase.GetString(reader, "ScopeMode")),
        ScopeCount = HrmsDatabase.GetInt(reader, "ScopeCount"),
        CompanyId = HrmsDatabase.GetNullableInt(reader, "CompanyId")
    };
}
