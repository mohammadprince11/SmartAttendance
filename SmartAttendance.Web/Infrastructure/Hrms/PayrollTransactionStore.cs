using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حركات المسير (نمط كيان «حركات الدخل/الإقتطاع/العمل الإضافي»): إدخالات مالية لكل
/// موظف×فترة (سنة/شهر) — دخل (مكافأة/حافز/بدل لمرة)، اقتطاع، أوفرتايم — تُغذّي احتساب
/// المسير كبنود إضافية على الأساسي والعلاوات. المخزن عام (TxType) وأول شاشة مبنية «الدخل».
/// </summary>
public static class PayrollTransactionStore
{
    public const string Income = "Income";
    public const string Deduction = "Deduction";
    public const string Overtime = "Overtime";

    public static string TypeLabel(string t) => t switch
    {
        "Income" => "دخل",
        "Deduction" => "اقتطاع",
        "Overtime" => "عمل إضافي",
        _ => t
    };

    public sealed class Transaction
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public int? SalaryItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string TxType { get; set; } = "Income";
        public bool Taxable { get; set; } = true;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }

        public string PeriodText => $"{Month:00}/{Year}";
        public bool IsAddition => TxType is "Income" or "Overtime";
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
""");
    }

    public static async Task<List<Transaction>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string txType, string? search)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT t.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName
FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
WHERE t.[Year] = @Y AND t.[Month] = @M AND t.TxType = @Type
ORDER BY t.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@Type", txType);
            },
            Read);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var v = search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.ItemName.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    /// <summary>حركات فترة معينة لنوع معيّن — للاحتساب بالمسير (بلا join).</summary>
    public static async Task<List<Transaction>> ForPeriodAsync(
        ApplicationDbContext dbContext, int year, int month, string txType)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM PayrollTransactions WHERE [Year] = @Y AND [Month] = @M AND TxType = @Type;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
                HrmsDatabase.AddParameter(command, "@Type", txType);
            },
            reader => new Transaction
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                TxType = HrmsDatabase.GetString(reader, "TxType"),
                Taxable = HrmsDatabase.GetBool(reader, "Taxable")
            });
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, Transaction tx, string userName)
    {
        await EnsureAsync(dbContext);
        if (tx.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE PayrollTransactions
SET EmployeeId = @Emp, [Year] = @Y, [Month] = @M, SalaryItemId = @Item,
    ItemName = @Name, Amount = @Amount, TxType = @Type, Taxable = @Taxable, Note = @Note
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", tx.Id);
                    Add(command, tx);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO PayrollTransactions (EmployeeId, [Year], [Month], SalaryItemId, ItemName, Amount, TxType, Taxable, Note, CreatedBy)
VALUES (@Emp, @Y, @M, @Item, @Name, @Amount, @Type, @Taxable, @Note, @By);
""",
                command =>
                {
                    Add(command, tx);
                    HrmsDatabase.AddParameter(command, "@By", userName);
                });
        }
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PayrollTransactions WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static Transaction Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        Year = HrmsDatabase.GetInt(reader, "Year"),
        Month = HrmsDatabase.GetInt(reader, "Month"),
        SalaryItemId = HrmsDatabase.GetNullableInt(reader, "SalaryItemId"),
        ItemName = HrmsDatabase.GetString(reader, "ItemName"),
        Amount = reader["Amount"] is decimal a ? a : 0,
        TxType = HrmsDatabase.GetString(reader, "TxType") is { Length: > 0 } t ? t : "Income",
        Taxable = HrmsDatabase.GetBool(reader, "Taxable"),
        Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
    };

    private static void Add(System.Data.Common.DbCommand command, Transaction tx)
    {
        HrmsDatabase.AddParameter(command, "@Emp", tx.EmployeeId);
        HrmsDatabase.AddParameter(command, "@Y", tx.Year);
        HrmsDatabase.AddParameter(command, "@M", tx.Month);
        HrmsDatabase.AddParameter(command, "@Item", (object?)tx.SalaryItemId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Name", tx.ItemName);
        HrmsDatabase.AddParameter(command, "@Amount", tx.Amount);
        HrmsDatabase.AddParameter(command, "@Type", tx.TxType);
        HrmsDatabase.AddParameter(command, "@Taxable", tx.Taxable ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Note", (object?)tx.Note ?? DBNull.Value);
    }
}
