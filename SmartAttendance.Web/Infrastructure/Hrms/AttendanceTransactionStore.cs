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
    }

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
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
            });
    }
}
