using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حركات المسير (مطابقة كيان «حركات الدخل/الإقتطاع/العمل الإضافي»): إدخالات مالية لكل
/// موظف تُغذّي المسير. تحمل كل خصائص كيان: نوع الدفعة (داخل/خارج الراتب)، الأثر الرجعي
/// (+تحديث الضمان)، الجدولة/الأقساط، مركز التكلفة، المرفق، الرقم المرجعي، المصدر، والحالة.
/// نمط self-healing (CREATE + ALTER ADD idempotent). التوسعة الخلفية (توليد الأقساط،
/// إعادة حساب الضمان، سير الموافقات) تُبنى تدريجياً؛ الحقول كلها ملتقطة ومخزّنة.
/// </summary>
public static class PayrollTransactionStore
{
    public const string Income = "Income";
    public const string Deduction = "Deduction";
    public const string Overtime = "Overtime";
    public const string SalaryDays = "SalaryDays";
    public const string LeaveEncashment = "LeaveEncashment";

    /// <summary>معامل بدل العمل الإضافي الافتراضي (1.5× الأجر الساعي) عند عدم تحديده.</summary>
    public const decimal DefaultRateFactor = 1.5m;

    public static string TypeLabel(string t) => t switch
    {
        "Income" => "دخل",
        "Deduction" => "اقتطاع",
        "Overtime" => "عمل إضافي",
        "SalaryDays" => "تعديل أيام الراتب",
        "LeaveEncashment" => "بدل إجازة",
        _ => t
    };

    public static string StatusLabel(string s) => s switch
    {
        "Draft" => "مسودة",
        "Approved" => "معتمد",
        "Rejected" => "مرفوض",
        _ => s
    };

    public sealed class Transaction
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public int? SalaryItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string TxType { get; set; } = "Income";
        public bool Taxable { get; set; } = true;

        // عمل إضافي (Overtime): ساعات × معامل بدل. المبلغ يُحتسب بالمسير من الأجر
        // الساعي للموظف × الساعات × المعامل. إن كانت الساعات فارغة يُستخدم Amount كقيمة يدوية.
        public decimal? Hours { get; set; }
        public decimal? RateFactor { get; set; }

        // تعديل أيام الراتب (SalaryDays): عدد أيام موقّع — موجب يضيف أياماً، سالب يخصمها.
        // المبلغ يُحتسب بالمسير = الأيام × الأجر اليومي (الأساسي ÷ 30). Amount يدوي بديل.
        public decimal? Days { get; set; }

        // نوع الدفعة
        public string PaymentType { get; set; } = "InSalary";   // InSalary | OutSalary

        // تواريخ
        public DateOnly? TransactionDate { get; set; }
        public DateOnly? EffectiveDate { get; set; }

        // غير محدود / فترة صلاحية
        public bool IsUnlimited { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }

        // بأثر رجعي
        public bool IsRetroactive { get; set; }
        public DateOnly? RetroactiveDate { get; set; }
        public bool UpdateSocialSecurity { get; set; }
        public DateOnly? SocialSecurityToDate { get; set; }
        public DateOnly? SalaryFromDate { get; set; }
        public DateOnly? SalaryToDate { get; set; }

        // جدولة / أقساط
        public bool IsScheduled { get; set; }
        public string? InstallmentMode { get; set; }            // ByValue | ByMonth
        public int? InstallmentCount { get; set; }
        public decimal? InstallmentAmount { get; set; }
        public int? InstallmentMonths { get; set; }
        public DateOnly? FirstInstallmentDate { get; set; }

        // مركز التكلفة
        public bool ChangeCostCenter { get; set; }
        public string? CostCenter { get; set; }

        // مرفق / ملاحظة / مصدر / حالة
        public string? AttachmentName { get; set; }
        public string? AttachmentPath { get; set; }
        public string? Note { get; set; }
        public string Source { get; set; } = "مباشر";
        public string Status { get; set; } = "Approved";
        public bool IsLocked { get; set; }
        public int? LockedRunId { get; set; }
        public DateTime CreatedAt { get; set; }

        public string PeriodText => $"{Month:00}/{Year}";
        public bool IsAddition => TxType is "Income" or "Overtime" or "LeaveEncashment";
        public string PaymentTypeLabel => PaymentType == "OutSalary" ? "خارج الراتب" : "داخل الراتب";
        public string StatusText => StatusLabel(Status);
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PayrollTransactions', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollTransactions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        SalaryItemId int NULL,
        ItemName nvarchar(200) NOT NULL,
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        TxType nvarchar(20) NOT NULL DEFAULT(N'Income'),
        Taxable bit NOT NULL DEFAULT(1),
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL
    );
    CREATE INDEX IX_PayrollTransactions_Period ON PayrollTransactions ([Year], [Month], TxType);
    CREATE INDEX IX_PayrollTransactions_Employee ON PayrollTransactions (EmployeeId);
END;

-- توسعة مطابقة كيان (idempotent)
IF COL_LENGTH('PayrollTransactions','ReferenceNo') IS NULL ALTER TABLE PayrollTransactions ADD ReferenceNo nvarchar(40) NULL;
IF COL_LENGTH('PayrollTransactions','PaymentType') IS NULL ALTER TABLE PayrollTransactions ADD PaymentType nvarchar(20) NOT NULL CONSTRAINT DF_PT_PaymentType DEFAULT(N'InSalary');
IF COL_LENGTH('PayrollTransactions','TransactionDate') IS NULL ALTER TABLE PayrollTransactions ADD TransactionDate date NULL;
IF COL_LENGTH('PayrollTransactions','EffectiveDate') IS NULL ALTER TABLE PayrollTransactions ADD EffectiveDate date NULL;
IF COL_LENGTH('PayrollTransactions','IsUnlimited') IS NULL ALTER TABLE PayrollTransactions ADD IsUnlimited bit NOT NULL CONSTRAINT DF_PT_Unlimited DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','ValidFrom') IS NULL ALTER TABLE PayrollTransactions ADD ValidFrom date NULL;
IF COL_LENGTH('PayrollTransactions','ValidTo') IS NULL ALTER TABLE PayrollTransactions ADD ValidTo date NULL;
IF COL_LENGTH('PayrollTransactions','IsRetroactive') IS NULL ALTER TABLE PayrollTransactions ADD IsRetroactive bit NOT NULL CONSTRAINT DF_PT_Retro DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','RetroactiveDate') IS NULL ALTER TABLE PayrollTransactions ADD RetroactiveDate date NULL;
IF COL_LENGTH('PayrollTransactions','UpdateSocialSecurity') IS NULL ALTER TABLE PayrollTransactions ADD UpdateSocialSecurity bit NOT NULL CONSTRAINT DF_PT_UpdSS DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','SocialSecurityToDate') IS NULL ALTER TABLE PayrollTransactions ADD SocialSecurityToDate date NULL;
IF COL_LENGTH('PayrollTransactions','SalaryFromDate') IS NULL ALTER TABLE PayrollTransactions ADD SalaryFromDate date NULL;
IF COL_LENGTH('PayrollTransactions','SalaryToDate') IS NULL ALTER TABLE PayrollTransactions ADD SalaryToDate date NULL;
IF COL_LENGTH('PayrollTransactions','IsScheduled') IS NULL ALTER TABLE PayrollTransactions ADD IsScheduled bit NOT NULL CONSTRAINT DF_PT_Sched DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','InstallmentMode') IS NULL ALTER TABLE PayrollTransactions ADD InstallmentMode nvarchar(20) NULL;
IF COL_LENGTH('PayrollTransactions','InstallmentCount') IS NULL ALTER TABLE PayrollTransactions ADD InstallmentCount int NULL;
IF COL_LENGTH('PayrollTransactions','InstallmentAmount') IS NULL ALTER TABLE PayrollTransactions ADD InstallmentAmount decimal(18,2) NULL;
IF COL_LENGTH('PayrollTransactions','InstallmentMonths') IS NULL ALTER TABLE PayrollTransactions ADD InstallmentMonths int NULL;
IF COL_LENGTH('PayrollTransactions','FirstInstallmentDate') IS NULL ALTER TABLE PayrollTransactions ADD FirstInstallmentDate date NULL;
IF COL_LENGTH('PayrollTransactions','ChangeCostCenter') IS NULL ALTER TABLE PayrollTransactions ADD ChangeCostCenter bit NOT NULL CONSTRAINT DF_PT_CCC DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','CostCenter') IS NULL ALTER TABLE PayrollTransactions ADD CostCenter nvarchar(150) NULL;
IF COL_LENGTH('PayrollTransactions','AttachmentName') IS NULL ALTER TABLE PayrollTransactions ADD AttachmentName nvarchar(260) NULL;
IF COL_LENGTH('PayrollTransactions','AttachmentPath') IS NULL ALTER TABLE PayrollTransactions ADD AttachmentPath nvarchar(500) NULL;
IF COL_LENGTH('PayrollTransactions','Source') IS NULL ALTER TABLE PayrollTransactions ADD Source nvarchar(50) NOT NULL CONSTRAINT DF_PT_Source DEFAULT(N'مباشر');
IF COL_LENGTH('PayrollTransactions','Status') IS NULL ALTER TABLE PayrollTransactions ADD Status nvarchar(30) NOT NULL CONSTRAINT DF_PT_Status DEFAULT(N'Approved');
IF COL_LENGTH('PayrollTransactions','IsLocked') IS NULL ALTER TABLE PayrollTransactions ADD IsLocked bit NOT NULL CONSTRAINT DF_PT_IsLocked DEFAULT(0);
IF COL_LENGTH('PayrollTransactions','LockedRunId') IS NULL ALTER TABLE PayrollTransactions ADD LockedRunId int NULL;
IF COL_LENGTH('PayrollTransactions','Hours') IS NULL ALTER TABLE PayrollTransactions ADD Hours decimal(9,2) NULL;
IF COL_LENGTH('PayrollTransactions','RateFactor') IS NULL ALTER TABLE PayrollTransactions ADD RateFactor decimal(6,3) NULL;
IF COL_LENGTH('PayrollTransactions','Days') IS NULL ALTER TABLE PayrollTransactions ADD Days decimal(9,2) NULL;
""");
    }

    public static async Task<List<Transaction>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int year, int month, string txType, string? search,
        int? salaryItemId = null, string? status = null, string? source = null, bool? locked = null,
        int? employeeId = null, string? paymentType = null, string? department = null,
        string? branch = null, string? position = null, DateOnly? dateFrom = null,
        DateOnly? dateTo = null, string? referenceNo = null, decimal? minAmount = null,
        decimal? maxAmount = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Transaction>();
        await EnsureAsync(dbContext);

        var predicates = new List<string>
        {
            "t.[Year] = @Y",
            "t.[Month] = @M",
            "t.TxType = @Type",
            EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")
        };

        if (locked.HasValue) predicates.Add("ISNULL(t.IsLocked, 0) = @Locked");
        if (salaryItemId is > 0) predicates.Add("t.SalaryItemId = @SalaryItemId");
        if (!string.IsNullOrWhiteSpace(status)) predicates.Add("t.Status = @Status");
        if (!string.IsNullOrWhiteSpace(source)) predicates.Add("t.Source = @Source");
        if (employeeId is > 0) predicates.Add("t.EmployeeId = @EmployeeId");
        if (!string.IsNullOrWhiteSpace(paymentType)) predicates.Add("t.PaymentType = @PaymentType");
        if (!string.IsNullOrWhiteSpace(department)) predicates.Add("d.Name = @Department");
        if (!string.IsNullOrWhiteSpace(branch)) predicates.Add("b.Name = @Branch");
        if (!string.IsNullOrWhiteSpace(position)) predicates.Add("e.Position = @Position");
        if (dateFrom.HasValue) predicates.Add("t.TransactionDate >= @DateFrom");
        if (dateTo.HasValue) predicates.Add("t.TransactionDate <= @DateTo");
        if (!string.IsNullOrWhiteSpace(referenceNo)) predicates.Add("t.ReferenceNo LIKE @ReferenceNo");
        if (minAmount.HasValue) predicates.Add("t.Amount >= @MinAmount");
        if (maxAmount.HasValue) predicates.Add("t.Amount <= @MaxAmount");
        if (!string.IsNullOrWhiteSpace(search))
        {
            predicates.Add("(e.EmployeeNo LIKE @Search OR e.FullName LIKE @Search OR t.ItemName LIKE @Search OR t.ReferenceNo LIKE @Search)");
        }

        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT t.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName, ISNULL(e.Position, N'') AS Position
FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE {string.Join(" AND ", predicates)}
ORDER BY t.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@Type", txType);
                if (locked.HasValue) HrmsDatabase.AddParameter(command, "@Locked", locked.Value ? 1 : 0);
                if (salaryItemId is > 0) HrmsDatabase.AddParameter(command, "@SalaryItemId", salaryItemId.Value);
                if (!string.IsNullOrWhiteSpace(status)) HrmsDatabase.AddParameter(command, "@Status", status.Trim());
                if (!string.IsNullOrWhiteSpace(source)) HrmsDatabase.AddParameter(command, "@Source", source.Trim());
                if (employeeId is > 0) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId.Value);
                if (!string.IsNullOrWhiteSpace(paymentType)) HrmsDatabase.AddParameter(command, "@PaymentType", paymentType.Trim());
                if (!string.IsNullOrWhiteSpace(department)) HrmsDatabase.AddParameter(command, "@Department", department.Trim());
                if (!string.IsNullOrWhiteSpace(branch)) HrmsDatabase.AddParameter(command, "@Branch", branch.Trim());
                if (!string.IsNullOrWhiteSpace(position)) HrmsDatabase.AddParameter(command, "@Position", position.Trim());
                if (dateFrom.HasValue) HrmsDatabase.AddParameter(command, "@DateFrom", dateFrom.Value.ToDateTime(TimeOnly.MinValue));
                if (dateTo.HasValue) HrmsDatabase.AddParameter(command, "@DateTo", dateTo.Value.ToDateTime(TimeOnly.MinValue));
                if (!string.IsNullOrWhiteSpace(referenceNo)) HrmsDatabase.AddParameter(command, "@ReferenceNo", $"%{referenceNo.Trim()}%");
                if (minAmount.HasValue) HrmsDatabase.AddParameter(command, "@MinAmount", minAmount.Value);
                if (maxAmount.HasValue) HrmsDatabase.AddParameter(command, "@MaxAmount", maxAmount.Value);
                if (!string.IsNullOrWhiteSpace(search)) HrmsDatabase.AddParameter(command, "@Search", $"%{search.Trim()}%");
            },
            Read);
    }

    /// <summary>
    /// قفل الحركات (نمط كيان — لكل حركة لا للفترة): عند قفل مسير، تُقفل الحركات
    /// المعتمدة داخل الراتب لفترته غير المقفلة سابقاً. الحركات الجديدة بعده تبقى
    /// «غير مقفلة» (تدخل مسيراً لاحقاً) — فيبقى بإمكانك إضافة حركات دائماً.
    /// </summary>
    public static async Task LockForRunAsync(ApplicationDbContext dbContext, int runId, int year, int month)
    {
        await EnsureAsync(dbContext);
        // ⚠️ **عزل الشركات**: كان القفل يشمل حركات **كل الشركات** للشهر (WHERE Year+Month
        // فقط)، فقفلُ مسير شركة A يقفل حركات شركة B بنفس الشهر. الآن يُحصر بموظفي الدفعة
        // نفسها: شركةُ الدفعة (join Employees ↔ PayrollRuns.CompanyId) وأعضاء نطاقها إن
        // وُجدوا (PayrollRunScopeMembers). دفعةٌ بلا شركة (تاريخية أحادية الشركة) وبلا
        // أعضاء نطاق تبقى على السلوك القديم للتوافق. النطاق مشتقٌّ من الدفعة داخل المتجر
        // فيتعذّر إغفاله من المستدعي.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE t
SET t.IsLocked = 1, t.LockedRunId = @Run
FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
INNER JOIN PayrollRuns r ON r.Id = @Run
WHERE t.[Year] = @Y AND t.[Month] = @M
  AND ISNULL(t.Status, N'Approved') = N'Approved'
  AND ISNULL(t.IsLocked, 0) = 0
  AND ((ISNULL(r.RunType,N'Regular')=N'Regular'
        AND ISNULL(t.PaymentType,N'InSalary')=N'InSalary' AND ISNULL(t.IsRetroactive,0)=0)
    OR (r.RunType=N'OffCycle'
        AND ISNULL(t.PaymentType,N'InSalary')=N'OutSalary' AND ISNULL(t.IsRetroactive,0)=0)
    OR (r.RunType=N'Retroactive' AND ISNULL(t.IsRetroactive,0)=1))
  AND (r.CompanyId IS NULL OR e.CompanyId = r.CompanyId)
  AND (NOT EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId = @Run)
       OR EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId = @Run AND s.EmployeeId = t.EmployeeId));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Run", runId);
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
            });
    }

    /// <summary>
    /// فكّ قفل حركات دفعةٍ عند إلغاء قفلها (Locked ← Calculated). يُحصر بالحركات التي
    /// قفلتها هذه الدفعة تحديداً عبر <c>LockedRunId</c> — فلا يمسّ حركات دفعات أخرى ولا
    /// حركات دخلت النطاق بعد القفل. عكسٌ دقيق لـ<see cref="LockForRunAsync"/> وآمن للعزل.
    /// </summary>
    public static async Task UnlockForRunAsync(ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE PayrollTransactions SET IsLocked = 0, LockedRunId = NULL WHERE LockedRunId = @Run;",
            command => HrmsDatabase.AddParameter(command, "@Run", runId));
    }

    /// <summary>هل حركة بعينها مقفلة؟ (دخلت مسيراً مقفلاً) — لحماية التعديل/الحذف.</summary>
    public static async Task<bool> IsLockedAsync(ApplicationDbContext dbContext, CompanyScope scope, int id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return false;
        await EnsureAsync(dbContext);
        var v = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"SELECT CAST(ISNULL(t.IsLocked,0) AS int) FROM PayrollTransactions t INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE t.Id = @Id AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        return v == 1;
    }

    /// <summary>حركات فترة/نوع للاحتساب بالمسير — المعتمدة داخل الراتب فقط.</summary>
    public static async Task<List<Transaction>> ForPeriodAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int year, int month, string txType,
        int? runId = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Transaction>();
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT t.* FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE t.[Year] = @Y AND t.[Month] = @M AND t.TxType = @Type
  AND ISNULL(PaymentType, N'InSalary') = N'InSalary'
  AND ISNULL(Status, N'Approved') = N'Approved'
  AND ISNULL(IsRetroactive,0)=0
  AND ISNULL(IsLocked,0)=0
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
  AND (@RunId IS NULL
       OR NOT EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId)
       OR EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId AND s.EmployeeId=t.EmployeeId));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@Type", txType);
                HrmsDatabase.AddParameter(command, "@RunId", (object?)runId ?? DBNull.Value);
            },
            reader => new Transaction
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                TxType = HrmsDatabase.GetString(reader, "TxType"),
                Taxable = HrmsDatabase.GetBool(reader, "Taxable"),
                Hours = reader["Hours"] is decimal h ? h : null,
                RateFactor = reader["RateFactor"] is decimal rf ? rf : null,
                Days = reader["Days"] is decimal dy ? dy : null
            });
    }

    /// <summary>
    /// بوابة فحص ما قبل المسير (نظير شاشة كيان الاعتراضية «تم العثور على حركات غير
    /// مقفلة و/أو طلبات قيد الموافقة غير مشمولة في حساب الرواتب»): حركات الفترة
    /// **المسودّة** (غير المعتمدة) داخل الراتب لأعضاء نطاق الدفعة — هي ما سيفوته
    /// الاحتساب بصمت لأن <see cref="ForPeriodAsync"/> لا يقرأ إلا المعتمد.
    ///
    /// تعرضها الشاشة قبل التنفيذ لا بعده: من يحتسب ثم يكتشف الفاقد يعيد الاحتساب
    /// مرّتين؛ ومن يراه أولاً يعتمد أو يتخطّى عن علم.
    /// </summary>
    public static async Task<List<Transaction>> PreflightDraftsAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int year, int month, int? runId = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Transaction>();
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT t.Id, t.EmployeeId, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS EmployeeName,
       t.TxType, ISNULL(t.ItemName, N'') AS ItemName, t.Amount, ISNULL(t.ReferenceNo, N'') AS ReferenceNo,
       ISNULL(t.Status, N'Approved') AS Status
FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE t.[Year] = @Y AND t.[Month] = @M
  AND ISNULL(t.PaymentType, N'InSalary') = N'InSalary'
  AND ISNULL(t.Status, N'Approved') <> N'Approved'
  AND ISNULL(t.Status, N'Approved') <> N'Rejected'
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
  AND (@RunId IS NULL
       OR NOT EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId)
       OR EXISTS (SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId AND s.EmployeeId=t.EmployeeId))
ORDER BY e.EmployeeNo, t.Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@RunId", (object?)runId ?? DBNull.Value);
            },
            reader => new Transaction
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName"),
                TxType = HrmsDatabase.GetString(reader, "TxType"),
                ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                Year = year,
                Month = month
            });
    }

    public static async Task<int> SaveAsync(
        ApplicationDbContext dbContext, CompanyScope scope, Transaction tx, string userName)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync()
            : null;
        try
        {
            await ValidateEmployeeIdsAsync(dbContext, scope, new[] { tx.EmployeeId });
            if (tx.Id > 0)
            {
                await ValidateTransactionIdsAsync(dbContext, scope, new[] { tx.Id }, requireUnlocked: true);
                await HrmsDatabase.ExecuteAsync(dbContext, UpdateSql, command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", tx.Id);
                    Add(command, tx);
                });
                if (ownTransaction != null) await ownTransaction.CommitAsync();
                return tx.Id;
            }

            if (string.IsNullOrWhiteSpace(tx.ReferenceNo))
                tx.ReferenceNo = await GenerateReferenceNoAsync(dbContext, tx.TxType);

            var id = await HrmsDatabase.ScalarAsync<int>(dbContext, InsertSql + " SELECT CAST(SCOPE_IDENTITY() AS int);", command =>
            {
                Add(command, tx);
                HrmsDatabase.AddParameter(command, "@Ref", tx.ReferenceNo);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
            if (ownTransaction != null) await ownTransaction.CommitAsync();
            return id;
        }
        finally
        {
            if (ownTransaction != null) await ownTransaction.DisposeAsync();
        }
    }

    /// <summary>دخل جماعي (نمط كيان): إنشاء نفس الحركة لعدة موظفين دفعة واحدة.</summary>
    public static async Task<int> SaveManyAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> employeeIds, Transaction template, string userName)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        var distinctIds = employeeIds.Distinct().ToArray();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await ValidateEmployeeIdsAsync(dbContext, scope, distinctIds);
        int n = 0;
        foreach (var empId in distinctIds)
        {
            template.Id = 0;
            template.EmployeeId = empId;
            template.ReferenceNo = await GenerateReferenceNoAsync(dbContext, template.TxType);
            await HrmsDatabase.ExecuteAsync(dbContext, InsertSql, command =>
            {
                Add(command, template);
                HrmsDatabase.AddParameter(command, "@Ref", template.ReferenceNo);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
            n++;
        }
        await transaction.CommitAsync();
        return n;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, CompanyScope scope, int id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await ValidateTransactionIdsAsync(dbContext, scope, new[] { id }, requireUnlocked: true);
        await HrmsDatabase.ExecuteAsync(dbContext,
            $"DELETE t FROM PayrollTransactions t INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE t.Id = @Id AND ISNULL(t.IsLocked,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        await transaction.CommitAsync();
    }

    /// <summary>إقفال/إلغاء قفل يدوي لحركات محددة (نمط كيان «إقفال العناصر المختارة»).</summary>
    public static async Task SetLockedAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids, bool locked)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await ValidateTransactionIdsAsync(dbContext, scope, ids, requireUnlocked: false);
        foreach (var chunk in ids.Chunk(200))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var setClause = locked ? "IsLocked = 1" : "IsLocked = 0, LockedRunId = NULL";
            await HrmsDatabase.ExecuteAsync(dbContext,
                $"UPDATE t SET {setClause} FROM PayrollTransactions t INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE t.Id IN ({inList}) AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
                command => { for (var i = 0; i < chunk.Length; i++) HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]); });
        }
        await transaction.CommitAsync();
    }

    public static async Task DeleteManyAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await ValidateTransactionIdsAsync(dbContext, scope, ids, requireUnlocked: true);
        foreach (var chunk in ids.Chunk(200))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(dbContext,
                $"DELETE t FROM PayrollTransactions t INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE t.Id IN ({inList}) AND ISNULL(t.IsLocked,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
                command => { for (var i = 0; i < chunk.Length; i++) HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]); });
        }
        await transaction.CommitAsync();
    }

    private static async Task ValidateEmployeeIdsAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> employeeIds)
    {
        var requested = employeeIds.Where(x => x > 0).Distinct().ToArray();
        if (requested.Length != employeeIds.Distinct().Count() || requested.Length == 0)
            throw new InvalidOperationException("Payroll transaction employee scope is invalid.");

        var valid = new HashSet<int>();
        foreach (var chunk in requested.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var rows = await HrmsDatabase.QueryAsync(dbContext,
                $"SELECT e.Id FROM Employees e WITH (UPDLOCK, HOLDLOCK) WHERE e.Id IN ({inList}) AND ISNULL(e.IsDeleted,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
                command =>
                {
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                },
                reader => HrmsDatabase.GetInt(reader, "Id"));
            valid.UnionWith(rows);
        }

        if (valid.Count != requested.Length)
            throw new InvalidOperationException("One or more payroll transaction employees are missing or outside company scope.");
    }

    private static async Task ValidateTransactionIdsAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> transactionIds, bool requireUnlocked)
    {
        var requested = transactionIds.Where(x => x > 0).Distinct().ToArray();
        if (requested.Length != transactionIds.Distinct().Count() || requested.Length == 0)
            throw new InvalidOperationException("Payroll transaction selection is invalid.");

        var valid = new HashSet<int>();
        foreach (var chunk in requested.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var unlocked = requireUnlocked ? " AND ISNULL(t.IsLocked,0)=0" : string.Empty;
            var rows = await HrmsDatabase.QueryAsync(dbContext,
                $"SELECT t.Id FROM PayrollTransactions t WITH (UPDLOCK, HOLDLOCK) INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE t.Id IN ({inList}){unlocked} AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
                command =>
                {
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                },
                reader => HrmsDatabase.GetInt(reader, "Id"));
            valid.UnionWith(rows);
        }

        if (valid.Count != requested.Length)
            throw new InvalidOperationException("One or more payroll transactions are locked, missing, or outside company scope.");
    }

    private static async Task<string> GenerateReferenceNoAsync(ApplicationDbContext dbContext, string txType)
    {
        var prefix = txType switch { "Deduction" => "DD", "Overtime" => "OT", "SalaryDays" => "SD", "LeaveEncashment" => "LV", _ => "IN" };
        prefix += $"{DateTime.Today:yy}-";
        var next = await HrmsDatabase.ScalarAsync<int>(dbContext,
            """
DECLARE @allocated TABLE ([Value] int NOT NULL);
MERGE PayrollTransactionSequences WITH (UPDLOCK, HOLDLOCK) AS target
USING (SELECT @Prefix AS Prefix) AS source
   ON target.Prefix = source.Prefix
WHEN MATCHED THEN UPDATE SET NextValue = target.NextValue + 1
WHEN NOT MATCHED THEN INSERT (Prefix, NextValue) VALUES (source.Prefix, 2)
OUTPUT CASE WHEN $action = 'INSERT' THEN 1 ELSE inserted.NextValue - 1 END INTO @allocated;
SELECT [Value] FROM @allocated;
""",
            command => HrmsDatabase.AddParameter(command, "@Prefix", prefix));
        return $"{prefix}{next:0000}";
    }

    private const string InsertSql = """
INSERT INTO PayrollTransactions
 (EmployeeId, [Year], [Month], SalaryItemId, ItemName, Amount, TxType, Taxable, PaymentType,
  TransactionDate, EffectiveDate, IsUnlimited, ValidFrom, ValidTo,
  IsRetroactive, RetroactiveDate, UpdateSocialSecurity, SocialSecurityToDate, SalaryFromDate, SalaryToDate,
  IsScheduled, InstallmentMode, InstallmentCount, InstallmentAmount, InstallmentMonths, FirstInstallmentDate,
  ChangeCostCenter, CostCenter, AttachmentName, AttachmentPath, Note, Source, Status, Hours, RateFactor, Days, ReferenceNo, CreatedBy)
VALUES
 (@Emp, @Y, @M, @Item, @Name, @Amount, @Type, @Taxable, @PaymentType,
  @TxDate, @EffDate, @Unlimited, @ValidFrom, @ValidTo,
  @Retro, @RetroDate, @UpdSS, @SSToDate, @SalFrom, @SalTo,
  @Sched, @InstMode, @InstCount, @InstAmount, @InstMonths, @FirstInst,
  @ChgCC, @CostCenter, @AttName, @AttPath, @Note, @Source, @Status, @Hours, @RateFactor, @Days, @Ref, @By);
""";

    private const string UpdateSql = """
UPDATE PayrollTransactions SET
  EmployeeId=@Emp, [Year]=@Y, [Month]=@M, SalaryItemId=@Item, ItemName=@Name, Amount=@Amount, TxType=@Type, Taxable=@Taxable,
  PaymentType=@PaymentType, TransactionDate=@TxDate, EffectiveDate=@EffDate, IsUnlimited=@Unlimited, ValidFrom=@ValidFrom, ValidTo=@ValidTo,
  IsRetroactive=@Retro, RetroactiveDate=@RetroDate, UpdateSocialSecurity=@UpdSS, SocialSecurityToDate=@SSToDate, SalaryFromDate=@SalFrom, SalaryToDate=@SalTo,
  IsScheduled=@Sched, InstallmentMode=@InstMode, InstallmentCount=@InstCount, InstallmentAmount=@InstAmount, InstallmentMonths=@InstMonths, FirstInstallmentDate=@FirstInst,
  ChangeCostCenter=@ChgCC, CostCenter=@CostCenter, AttachmentName=@AttName, AttachmentPath=@AttPath, Note=@Note, Source=@Source, Status=@Status, Hours=@Hours, RateFactor=@RateFactor, Days=@Days
WHERE Id=@Id;
""";

    private static Transaction Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        Branch = HrmsDatabase.GetString(reader, "BranchName"),
        Position = HrmsDatabase.GetString(reader, "Position"),
        Year = HrmsDatabase.GetInt(reader, "Year"),
        Month = HrmsDatabase.GetInt(reader, "Month"),
        SalaryItemId = HrmsDatabase.GetNullableInt(reader, "SalaryItemId"),
        ItemName = HrmsDatabase.GetString(reader, "ItemName"),
        Amount = reader["Amount"] is decimal a ? a : 0,
        TxType = HrmsDatabase.GetString(reader, "TxType") is { Length: > 0 } t ? t : "Income",
        Taxable = HrmsDatabase.GetBool(reader, "Taxable"),
        PaymentType = HrmsDatabase.GetString(reader, "PaymentType") is { Length: > 0 } p ? p : "InSalary",
        TransactionDate = HrmsDatabase.GetDateOnly(reader, "TransactionDate"),
        EffectiveDate = HrmsDatabase.GetDateOnly(reader, "EffectiveDate"),
        IsUnlimited = HrmsDatabase.GetBool(reader, "IsUnlimited"),
        ValidFrom = HrmsDatabase.GetDateOnly(reader, "ValidFrom"),
        ValidTo = HrmsDatabase.GetDateOnly(reader, "ValidTo"),
        IsRetroactive = HrmsDatabase.GetBool(reader, "IsRetroactive"),
        RetroactiveDate = HrmsDatabase.GetDateOnly(reader, "RetroactiveDate"),
        UpdateSocialSecurity = HrmsDatabase.GetBool(reader, "UpdateSocialSecurity"),
        SocialSecurityToDate = HrmsDatabase.GetDateOnly(reader, "SocialSecurityToDate"),
        SalaryFromDate = HrmsDatabase.GetDateOnly(reader, "SalaryFromDate"),
        SalaryToDate = HrmsDatabase.GetDateOnly(reader, "SalaryToDate"),
        IsScheduled = HrmsDatabase.GetBool(reader, "IsScheduled"),
        InstallmentMode = HrmsDatabase.GetString(reader, "InstallmentMode") is { Length: > 0 } im ? im : null,
        InstallmentCount = HrmsDatabase.GetNullableInt(reader, "InstallmentCount"),
        InstallmentAmount = reader["InstallmentAmount"] is decimal ia ? ia : null,
        InstallmentMonths = HrmsDatabase.GetNullableInt(reader, "InstallmentMonths"),
        FirstInstallmentDate = HrmsDatabase.GetDateOnly(reader, "FirstInstallmentDate"),
        ChangeCostCenter = HrmsDatabase.GetBool(reader, "ChangeCostCenter"),
        CostCenter = HrmsDatabase.GetString(reader, "CostCenter") is { Length: > 0 } cc ? cc : null,
        AttachmentName = HrmsDatabase.GetString(reader, "AttachmentName") is { Length: > 0 } an ? an : null,
        AttachmentPath = HrmsDatabase.GetString(reader, "AttachmentPath") is { Length: > 0 } ap ? ap : null,
        Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
        Source = HrmsDatabase.GetString(reader, "Source") is { Length: > 0 } s ? s : "مباشر",
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : "Approved",
        IsLocked = HrmsDatabase.GetBool(reader, "IsLocked"),
        LockedRunId = HrmsDatabase.GetNullableInt(reader, "LockedRunId"),
        Hours = reader["Hours"] is decimal h ? h : null,
        RateFactor = reader["RateFactor"] is decimal rf ? rf : null,
        Days = reader["Days"] is decimal dy ? dy : null,
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
    };

    private static void Add(System.Data.Common.DbCommand command, Transaction tx)
    {
        object? D(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        HrmsDatabase.AddParameter(command, "@Emp", tx.EmployeeId);
        HrmsDatabase.AddParameter(command, "@Y", tx.Year);
        HrmsDatabase.AddParameter(command, "@M", tx.Month);
        HrmsDatabase.AddParameter(command, "@Item", (object?)tx.SalaryItemId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Name", tx.ItemName);
        HrmsDatabase.AddParameter(command, "@Amount", tx.Amount);
        HrmsDatabase.AddParameter(command, "@Type", tx.TxType);
        HrmsDatabase.AddParameter(command, "@Taxable", tx.Taxable ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@PaymentType", tx.PaymentType);
        HrmsDatabase.AddParameter(command, "@TxDate", D(tx.TransactionDate));
        HrmsDatabase.AddParameter(command, "@EffDate", D(tx.EffectiveDate));
        HrmsDatabase.AddParameter(command, "@Unlimited", tx.IsUnlimited ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@ValidFrom", D(tx.ValidFrom));
        HrmsDatabase.AddParameter(command, "@ValidTo", D(tx.ValidTo));
        HrmsDatabase.AddParameter(command, "@Retro", tx.IsRetroactive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@RetroDate", D(tx.RetroactiveDate));
        HrmsDatabase.AddParameter(command, "@UpdSS", tx.UpdateSocialSecurity ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@SSToDate", D(tx.SocialSecurityToDate));
        HrmsDatabase.AddParameter(command, "@SalFrom", D(tx.SalaryFromDate));
        HrmsDatabase.AddParameter(command, "@SalTo", D(tx.SalaryToDate));
        HrmsDatabase.AddParameter(command, "@Sched", tx.IsScheduled ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@InstMode", (object?)tx.InstallmentMode ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@InstCount", (object?)tx.InstallmentCount ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@InstAmount", (object?)tx.InstallmentAmount ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@InstMonths", (object?)tx.InstallmentMonths ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@FirstInst", D(tx.FirstInstallmentDate));
        HrmsDatabase.AddParameter(command, "@ChgCC", tx.ChangeCostCenter ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CostCenter", (object?)tx.CostCenter ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@AttName", (object?)tx.AttachmentName ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@AttPath", (object?)tx.AttachmentPath ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Note", (object?)tx.Note ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Source", string.IsNullOrWhiteSpace(tx.Source) ? "مباشر" : tx.Source);
        HrmsDatabase.AddParameter(command, "@Status", string.IsNullOrWhiteSpace(tx.Status) ? "Approved" : tx.Status);
        HrmsDatabase.AddParameter(command, "@Hours", (object?)tx.Hours ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@RateFactor", (object?)tx.RateFactor ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Days", (object?)tx.Days ?? DBNull.Value);
    }
}
