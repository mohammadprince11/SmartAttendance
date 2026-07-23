using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حركات الحضور (نمط كيان — «AttendanceTransaction/الأثر المنفَّذ» بقسم 14 بدراسة
/// الحضور): الأثر غير-المخالفة الناتج عن اعتماد اقتراح قاعدة (إجازة/مغادرة/أوفرتايم/
/// دخل/اقتطاع). المخالفات تبقى بمسارها (EmployeeViolationCases). هذه الحركات تُغذّي
/// الرواتب لاحقاً: Overtime/Permission بالساعات، Income/Deduction بالمبلغ.
/// </summary>
public static class AttendanceTransactionStore
{
    public sealed class TransactionRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public int RecommendationId { get; set; }
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Hours { get; set; }
        public DateTime CreatedAt { get; set; }

        // جسر الرواتب (نمط كيان «انقل إلى وحدة الرواتب» + حالة «لم يتم نقلها بعد»)
        public string PayrollStatus { get; set; } = NotTransferred;
        public int? PayrollTransactionId { get; set; }
        public DateTime? TransferredAt { get; set; }
        public string TransferredBy { get; set; } = string.Empty;

        public bool IsTransferred => PayrollStatus == Transferred;
        public bool CanTransfer => !IsTransferred && IsTransferable(ActionType);
    }

    public const string NotTransferred = "NotTransferred";
    public const string Transferred = "Transferred";

    /// <summary>
    /// أنواع الأثر التي تُرحَّل للرواتب. المغادرة (Permission) والإجازة (Leave) مستثناتان
    /// عمداً: عند كيان تذهبان لوحدة المغادرات/الإجازات لا للرواتب، ونمذجتهما عندنا لم تُحسم.
    /// </summary>
    public static bool IsTransferable(string actionType) =>
        actionType is "Overtime" or "Income" or "Deduction";

    public static string PayrollStatusLabel(string status) => status switch
    {
        Transferred => "مُرحَّلة",
        _ => "لم تُرحَّل بعد"
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AttendanceTransactions', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceTransactions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        RecommendationId int NOT NULL,
        RuleId int NOT NULL,
        RuleName nvarchar(200) NOT NULL DEFAULT(N''),
        ActionType nvarchar(20) NOT NULL,
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        Amount decimal(12,2) NOT NULL DEFAULT(0),
        Hours decimal(6,2) NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_AttendanceTransactions_Recommendation
        ON AttendanceTransactions (RecommendationId);
END;

-- جسر الرواتب (idempotent)
IF COL_LENGTH('AttendanceTransactions','PayrollStatus') IS NULL
    ALTER TABLE AttendanceTransactions ADD PayrollStatus nvarchar(20) NOT NULL
        CONSTRAINT DF_AT_PayrollStatus DEFAULT(N'NotTransferred');
IF COL_LENGTH('AttendanceTransactions','PayrollTransactionId') IS NULL
    ALTER TABLE AttendanceTransactions ADD PayrollTransactionId int NULL;
IF COL_LENGTH('AttendanceTransactions','TransferredAt') IS NULL
    ALTER TABLE AttendanceTransactions ADD TransferredAt datetime2 NULL;
IF COL_LENGTH('AttendanceTransactions','TransferredBy') IS NULL
    ALTER TABLE AttendanceTransactions ADD TransferredBy nvarchar(150) NULL;
""");
    }

    /// <summary>إنشاء حركة من أثر قاعدة (ساعات للأوفرتايم/المغادرة، مبلغ للدخل/الاقتطاع).</summary>
    public static async Task<int> CreateAsync(ApplicationDbContext dbContext,
        int employeeId, DateOnly workDate, int recommendationId, int ruleId, string ruleName,
        string actionType, string actionText, decimal actionValue)
    {
        await EnsureAsync(dbContext);

        var isHours = actionType is "Overtime" or "Permission";
        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO AttendanceTransactions
    (EmployeeId, WorkDate, RecommendationId, RuleId, RuleName, ActionType, ActionText, Amount, Hours)
VALUES
    (@Employee, @Date, @Rec, @Rule, @RuleName, @ActionType, @ActionText, @Amount, @Hours);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", workDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Rec", recommendationId);
                HrmsDatabase.AddParameter(command, "@Rule", ruleId);
                HrmsDatabase.AddParameter(command, "@RuleName", ruleName);
                HrmsDatabase.AddParameter(command, "@ActionType", actionType);
                HrmsDatabase.AddParameter(command, "@ActionText", actionText);
                HrmsDatabase.AddParameter(command, "@Amount", isHours ? 0 : actionValue);
                HrmsDatabase.AddParameter(command, "@Hours", isHours ? actionValue : 0);
            });
    }

    public static async Task<List<TransactionRow>> ListAsync(
        ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT t.*, e.EmployeeNo, e.FullName
FROM AttendanceTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE t.WorkDate >= @From AND t.WorkDate <= @To
ORDER BY t.WorkDate DESC, e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new TransactionRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RecommendationId = HrmsDatabase.GetInt(reader, "RecommendationId"),
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                Hours = reader["Hours"] is decimal h ? h : 0,
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default,
                PayrollStatus = HrmsDatabase.GetString(reader, "PayrollStatus") is { Length: > 0 } ps ? ps : NotTransferred,
                PayrollTransactionId = HrmsDatabase.GetNullableInt(reader, "PayrollTransactionId"),
                TransferredAt = HrmsDatabase.GetDateTime(reader, "TransferredAt"),
                TransferredBy = HrmsDatabase.GetString(reader, "TransferredBy")
            });
    }

    /// <summary>
    /// صف «حركة مخالفة» — الأثر التأديبي المولّد من محرك الحضور. كيان يفصله عن
    /// الحركات المالية بتبويب فرعي مستقل بأعمدة مختلفة (قسم 29.ب بدراسة الحضور).
    /// </summary>
    public sealed class ViolationRow
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly EventDate { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ActionStatus { get; set; } = string.Empty;
        public decimal DeductionAmount { get; set; }
    }

    /// <summary>
    /// قضايا المخالفات المولّدة من محرك الحضور لشهر بعينه — مصدرها «محرك الحضور»
    /// تمييزاً عن القضايا المُدخلة يدوياً بمودل المخالفات.
    /// </summary>
    public static async Task<List<ViolationRow>> ListViolationsAsync(
        ApplicationDbContext dbContext, int year, int month)
    {
        await ViolationCaseSchema.EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT v.Id, v.ReferenceNo, v.EventDate, v.ViolationTitle, v.Status,
       v.ActionStatus, v.DeductionAmount,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
WHERE ISNULL(v.IsDeleted, 0) = 0
  AND v.Source = N'محرك الحضور'
  AND v.EventDate >= @From AND v.EventDate <= @To
ORDER BY v.EventDate DESC, v.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new ViolationRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                EventDate = HrmsDatabase.GetDateOnly(reader, "EventDate") ?? default,
                Title = HrmsDatabase.GetString(reader, "ViolationTitle"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                ActionStatus = HrmsDatabase.GetString(reader, "ActionStatus"),
                DeductionAmount = reader["DeductionAmount"] is decimal d ? d : 0
            });
    }

    private static async Task<List<TransactionRow>> ByIdsAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return new List<TransactionRow>();
        var inList = string.Join(",", ids.Select((_, i) => $"@P{i}"));
        var idArray = ids.ToArray();

        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT t.*, e.EmployeeNo, e.FullName
FROM AttendanceTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE t.Id IN ({inList});
""",
            command => { for (var i = 0; i < idArray.Length; i++) HrmsDatabase.AddParameter(command, $"@P{i}", idArray[i]); },
            reader => new TransactionRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RecommendationId = HrmsDatabase.GetInt(reader, "RecommendationId"),
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                Hours = reader["Hours"] is decimal h ? h : 0,
                PayrollStatus = HrmsDatabase.GetString(reader, "PayrollStatus") is { Length: > 0 } ps ? ps : NotTransferred,
                PayrollTransactionId = HrmsDatabase.GetNullableInt(reader, "PayrollTransactionId")
            });
    }

    /// <summary>
    /// ترحيل حركات حضور إلى حركات المسير (نمط كيان «انقل إلى وحدة الرواتب»).
    /// الخريطة: أوفرتايم ← <c>Overtime</c> بالساعات + معامل البدل الافتراضي (المبلغ يُحتسب
    /// بالمسير من الأجر الساعي)؛ دخل/اقتطاع ← <c>Income</c>/<c>Deduction</c> بالمبلغ.
    /// الحركة المُرحَّلة سابقاً أو غير القابلة للترحيل (مغادرة/إجازة) تُتخطّى بلا خطأ.
    /// لا نمنع الترحيل لشهر فيه مسير مقفل: <c>PayrollTransactionStore</c> يقفل الحركات
    /// لكل حركة لا للفترة، فالحركة الجديدة تبقى غير مقفلة وتدخل مسيراً لاحقاً.
    /// </summary>
    public static async Task<(int Transferred, int Skipped)> TransferToPayrollAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> ids, string userName)
    {
        await EnsureAsync(dbContext);
        var rows = await ByIdsAsync(dbContext, ids);

        int done = 0, skipped = 0;

        foreach (var row in rows)
        {
            if (!row.CanTransfer) { skipped++; continue; }

            var isOvertime = row.ActionType == "Overtime";
            var payrollId = await PayrollTransactionStore.SaveAsync(dbContext, new PayrollTransactionStore.Transaction
            {
                EmployeeId = row.EmployeeId,
                Year = row.WorkDate.Year,
                Month = row.WorkDate.Month,
                ItemName = string.IsNullOrWhiteSpace(row.RuleName) ? "حركة حضور" : row.RuleName,
                TxType = isOvertime ? PayrollTransactionStore.Overtime : row.ActionType,
                Amount = isOvertime ? 0 : row.Amount,
                Hours = isOvertime ? row.Hours : null,
                RateFactor = isOvertime ? PayrollTransactionStore.DefaultRateFactor : null,
                Taxable = true,
                PaymentType = "InSalary",
                Status = "Approved",
                TransactionDate = row.WorkDate,
                Note = row.ActionText,
                Source = "محرك الحضور"
            }, userName);

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE AttendanceTransactions
SET PayrollStatus = N'Transferred', PayrollTransactionId = @Payroll,
    TransferredAt = SYSUTCDATETIME(), TransferredBy = @By
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", row.Id);
                    HrmsDatabase.AddParameter(command, "@Payroll", payrollId);
                    HrmsDatabase.AddParameter(command, "@By", userName);
                });

            done++;
        }

        return (done, skipped);
    }

    /// <summary>
    /// إلغاء الترحيل (نظير «إزالة» بكيان): يحذف حركة المسير المرتبطة ويعيد الحالة.
    /// الحركة التي دخلت مسيراً مقفلاً لا تُلغى — تُتخطّى وتبقى مُرحَّلة.
    /// </summary>
    public static async Task<(int Undone, int Skipped)> UndoTransferAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> ids)
    {
        await EnsureAsync(dbContext);
        var rows = await ByIdsAsync(dbContext, ids);

        int done = 0, skipped = 0;

        foreach (var row in rows)
        {
            if (!row.IsTransferred) { skipped++; continue; }

            if (row.PayrollTransactionId is > 0)
            {
                if (await PayrollTransactionStore.IsLockedAsync(dbContext, row.PayrollTransactionId.Value))
                {
                    skipped++;
                    continue;
                }

                await PayrollTransactionStore.DeleteAsync(dbContext, row.PayrollTransactionId.Value);
            }

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE AttendanceTransactions
SET PayrollStatus = N'NotTransferred', PayrollTransactionId = NULL,
    TransferredAt = NULL, TransferredBy = NULL
WHERE Id = @Id;
""",
                command => HrmsDatabase.AddParameter(command, "@Id", row.Id));

            done++;
        }

        return (done, skipped);
    }
}
