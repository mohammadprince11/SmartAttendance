using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// القروض والسُّلف (مطابقة ZenHR «Loan Management» + كيان): دورة حياة قرض/سُلفة —
/// مبلغ إجمالي مقسّم على أقساط شهرية، بموافقة، ثم خصم شهري آلي يرحّل كـ«اقتطاع» لحركات
/// المسير عند استحقاق كل قسط، مع تتبّع الرصيد المتبقّي. نمط self-healing (CREATE + ALTER
/// ADD idempotent). القرض = مبلغ كبير بأقساط؛ السُّلفة = عادةً قسط واحد — نفس البنية.
/// </summary>
public static class LoanStore
{
    public const string Loan = "Loan";
    public const string Advance = "Advance";

    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Closed = "Closed";
    public const string Rejected = "Rejected";

    public static string TypeLabel(string t) => t == Advance ? "سُلفة" : "قرض";
    public static string StatusLabel(string s) => s switch
    {
        "Pending" => "قيد الموافقة",
        "Approved" => "معتمد (يُخصم)",
        "Closed" => "مُسدَّد/مُغلق",
        "Rejected" => "مرفوض",
        _ => s
    };

    public sealed class Loan_
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string LoanType { get; set; } = Loan;
        public decimal Amount { get; set; }
        public int InstallmentCount { get; set; } = 1;
        public decimal MonthlyAmount { get; set; }
        public int StartYear { get; set; }
        public int StartMonth { get; set; }
        public string? Reason { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = Pending;
        public string? AttachmentName { get; set; }
        public string? AttachmentPath { get; set; }
        public DateTime CreatedAt { get; set; }

        // محسوبة من الأقساط
        public decimal PaidAmount { get; set; }
        public int PaidCount { get; set; }
        public decimal Remaining => Math.Max(0, Amount - PaidAmount);

        public string TypeText => TypeLabel(LoanType);
        public string StatusText => StatusLabel(Status);
        public string StartPeriodText => $"{StartMonth:00}/{StartYear}";
    }

    public sealed class Installment
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public int SeqNo { get; set; }
        public int DueYear { get; set; }
        public int DueMonth { get; set; }
        public decimal Amount { get; set; }
        public bool IsPosted { get; set; }
        public int? PostedTransactionId { get; set; }
        public DateTime? PostedAt { get; set; }
        public string DueText => $"{DueMonth:00}/{DueYear}";
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeLoans', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeLoans
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo nvarchar(40) NULL,
        EmployeeId int NOT NULL,
        LoanType nvarchar(20) NOT NULL DEFAULT(N'Loan'),
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        InstallmentCount int NOT NULL DEFAULT(1),
        MonthlyAmount decimal(18,2) NOT NULL DEFAULT(0),
        StartYear int NOT NULL,
        StartMonth int NOT NULL,
        Reason nvarchar(200) NULL,
        Note nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL DEFAULT(N'Pending'),
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(400) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        ApprovedAt datetime2 NULL,
        ApprovedBy nvarchar(150) NULL
    );
    CREATE INDEX IX_EmployeeLoans_Employee ON EmployeeLoans (EmployeeId);
    CREATE INDEX IX_EmployeeLoans_Status ON EmployeeLoans (Status);
END;

IF OBJECT_ID('EmployeeLoanInstallments', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeLoanInstallments
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LoanId int NOT NULL,
        SeqNo int NOT NULL,
        DueYear int NOT NULL,
        DueMonth int NOT NULL,
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        IsPosted bit NOT NULL DEFAULT(0),
        PostedTransactionId int NULL,
        PostedAt datetime2 NULL
    );
    CREATE INDEX IX_EmployeeLoanInstallments_Loan ON EmployeeLoanInstallments (LoanId);
    CREATE INDEX IX_EmployeeLoanInstallments_Due ON EmployeeLoanInstallments (DueYear, DueMonth, IsPosted);
END;
""");
    }

    public sealed class EmployeeBasic
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>منتقي الموظفين — بنطاق الشركات: تسريب الـlookup يكشف هيكل شركةٍ أخرى.</summary>
    public static async Task<List<EmployeeBasic>> EmployeeBasicsAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<EmployeeBasic>();
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName
FROM Employees e
WHERE ISNULL(e.IsDeleted,0) = 0 AND ISNULL(e.IsActive,1) = 1
  AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY e.FullName;
""",
            command => { },
            reader => new EmployeeBasic
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName")
            });
    }

    public static async Task<List<Loan_>> ListAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope,
        int? employeeId = null, string? status = null, string? type = null, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Loan_>();
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT l.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName,
       (SELECT ISNULL(SUM(i.Amount),0) FROM EmployeeLoanInstallments i WHERE i.LoanId = l.Id AND i.IsPosted = 1) AS PaidAmount,
       (SELECT COUNT(1) FROM EmployeeLoanInstallments i WHERE i.LoanId = l.Id AND i.IsPosted = 1) AS PaidCount
FROM EmployeeLoans l
INNER JOIN Employees e ON e.Id = l.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
WHERE {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY l.CreatedAt DESC;
""",
            command => { },
            Read);

        if (employeeId is > 0) rows = rows.Where(r => r.EmployeeId == employeeId).ToList();
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(r => r.Status == status).ToList();
        if (!string.IsNullOrWhiteSpace(type)) rows = rows.Where(r => r.LoanType == type).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var v = search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                (r.Reason ?? "").Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.ReferenceNo.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    public static async Task<List<Installment>> InstallmentsAsync(ApplicationDbContext dbContext, int loanId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM EmployeeLoanInstallments WHERE LoanId = @Id ORDER BY SeqNo;",
            command => HrmsDatabase.AddParameter(command, "@Id", loanId),
            reader => new Installment
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                LoanId = HrmsDatabase.GetInt(reader, "LoanId"),
                SeqNo = HrmsDatabase.GetInt(reader, "SeqNo"),
                DueYear = HrmsDatabase.GetInt(reader, "DueYear"),
                DueMonth = HrmsDatabase.GetInt(reader, "DueMonth"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                IsPosted = HrmsDatabase.GetBool(reader, "IsPosted"),
                PostedTransactionId = reader["PostedTransactionId"] is int pt ? pt : null,
                PostedAt = HrmsDatabase.GetDateTime(reader, "PostedAt")
            });
    }

    /// <summary>ينشئ/يحدّث القرض ويولّد جدول الأقساط (لغير المُغلق). يرجع Id.</summary>
    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, Loan_ loan, string userName)
    {
        await EnsureAsync(dbContext);

        if (loan.InstallmentCount < 1) loan.InstallmentCount = 1;
        loan.MonthlyAmount = Math.Round(loan.Amount / loan.InstallmentCount, 2, MidpointRounding.AwayFromZero);

        int loanId;
        if (loan.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(dbContext, UpdateSql, command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", loan.Id);
                Add(command, loan);
            });
            loanId = loan.Id;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(loan.ReferenceNo))
                loan.ReferenceNo = await GenerateReferenceNoAsync(dbContext);
            loanId = await HrmsDatabase.ScalarAsync<int>(dbContext, InsertSql + " SELECT CAST(SCOPE_IDENTITY() AS int);", command =>
            {
                Add(command, loan);
                HrmsDatabase.AddParameter(command, "@Ref", loan.ReferenceNo);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        }

        await RegenerateScheduleAsync(dbContext, loanId, loan);
        return loanId;
    }

    /// <summary>يعيد توليد جدول الأقساط غير المرحّلة (لا يمسّ المرحّلة). آخر قسط يمتصّ فرق التقريب.</summary>
    private static async Task RegenerateScheduleAsync(ApplicationDbContext dbContext, int loanId, Loan_ loan)
    {
        // احذف الأقساط غير المرحّلة فقط حتى لا نلمس ما خُصم فعلاً.
        await HrmsDatabase.ExecuteAsync(dbContext,
            "DELETE FROM EmployeeLoanInstallments WHERE LoanId = @Id AND IsPosted = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", loanId));

        var posted = await HrmsDatabase.QueryAsync(dbContext,
            "SELECT SeqNo, Amount FROM EmployeeLoanInstallments WHERE LoanId = @Id AND IsPosted = 1;",
            command => HrmsDatabase.AddParameter(command, "@Id", loanId),
            reader => new { Seq = HrmsDatabase.GetInt(reader, "SeqNo"), Amt = reader["Amount"] is decimal a ? a : 0 });

        var paidSum = posted.Sum(p => p.Amt);
        var maxPostedSeq = posted.Count > 0 ? posted.Max(p => p.Seq) : 0;
        var remaining = Math.Max(0, loan.Amount - paidSum);

        var remainingCount = loan.InstallmentCount - maxPostedSeq;
        if (remainingCount < 1) return;

        var monthly = Math.Round(remaining / remainingCount, 2, MidpointRounding.AwayFromZero);
        var (year, month) = AddMonths(loan.StartYear, loan.StartMonth, maxPostedSeq);
        var acc = 0m;

        for (var i = 1; i <= remainingCount; i++)
        {
            var seq = maxPostedSeq + i;
            var amt = i == remainingCount ? Math.Round(remaining - acc, 2) : monthly;
            acc += monthly;
            var dy = year;
            var dm = month;
            await HrmsDatabase.ExecuteAsync(dbContext,
                "INSERT INTO EmployeeLoanInstallments (LoanId, SeqNo, DueYear, DueMonth, Amount) VALUES (@L, @S, @Y, @M, @A);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@L", loanId);
                    HrmsDatabase.AddParameter(command, "@S", seq);
                    HrmsDatabase.AddParameter(command, "@Y", dy);
                    HrmsDatabase.AddParameter(command, "@M", dm);
                    HrmsDatabase.AddParameter(command, "@A", amt);
                });
            (year, month) = AddMonths(year, month, 1);
        }
    }

    public static async Task<bool> SetStatusAsync(ApplicationDbContext dbContext, int id, string status, string userName)
    {
        await EnsureAsync(dbContext);
        var approvedStamp = status == Approved ? ", ApprovedAt = SYSUTCDATETIME(), ApprovedBy = @By" : "";
        await HrmsDatabase.ExecuteAsync(dbContext,
            $"UPDATE EmployeeLoans SET Status = @St{approvedStamp} WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@St", status);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return true;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        // لا نحذف قرضاً خُصم منه أي قسط.
        var postedCount = await HrmsDatabase.ScalarAsync<int>(dbContext,
            "SELECT COUNT(1) FROM EmployeeLoanInstallments WHERE LoanId = @Id AND IsPosted = 1;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        if (postedCount > 0) return;
        await HrmsDatabase.ExecuteAsync(dbContext, "DELETE FROM EmployeeLoanInstallments WHERE LoanId = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        await HrmsDatabase.ExecuteAsync(dbContext, "DELETE FROM EmployeeLoans WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>
    /// ترحيل الأقساط المستحقة لشهر معيّن: لكل قرض «معتمد» له قسط غير مرحّل يستحق ≤ الشهر
    /// الهدف، يُنشئ اقتطاعاً بحركات المسير ويُعلّم القسط مرحّلاً. يُغلق القرض عند تسديد آخر قسط.
    /// يرجع عدد الأقساط المُرحّلة.
    /// </summary>
    public static async Task<int> PostDueInstallmentsAsync(ApplicationDbContext dbContext, int year, int month, string userName)
    {
        await EnsureAsync(dbContext);
        var target = year * 12 + month;

        var due = await HrmsDatabase.QueryAsync(dbContext,
            """
SELECT i.Id AS InstId, i.LoanId, i.SeqNo, i.DueYear, i.DueMonth, i.Amount,
       l.EmployeeId, l.ReferenceNo, l.LoanType, l.InstallmentCount
FROM EmployeeLoanInstallments i
INNER JOIN EmployeeLoans l ON l.Id = i.LoanId
WHERE i.IsPosted = 0 AND l.Status = N'Approved'
  AND (i.DueYear * 12 + i.DueMonth) <= @T
ORDER BY i.DueYear, i.DueMonth, i.SeqNo;
""",
            command => HrmsDatabase.AddParameter(command, "@T", target),
            reader => new DueRow
            {
                InstId = HrmsDatabase.GetInt(reader, "InstId"),
                LoanId = HrmsDatabase.GetInt(reader, "LoanId"),
                SeqNo = HrmsDatabase.GetInt(reader, "SeqNo"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                LoanType = HrmsDatabase.GetString(reader, "LoanType"),
                InstallmentCount = HrmsDatabase.GetInt(reader, "InstallmentCount")
            });

        var posted = 0;
        foreach (var d in due)
        {
            var tx = new PayrollTransactionStore.Transaction
            {
                EmployeeId = d.EmployeeId,
                Year = year,
                Month = month,
                ItemName = $"قسط {(d.LoanType == Advance ? "سُلفة" : "قرض")} {d.ReferenceNo} ({d.SeqNo}/{d.InstallmentCount})",
                Amount = d.Amount,
                TxType = PayrollTransactionStore.Deduction,
                Taxable = false,
                PaymentType = "InSalary",
                Source = "قرض/سُلفة",
                Status = "Approved",
                Note = $"خصم آلي لقسط القرض {d.ReferenceNo}"
            };
            var txId = await PayrollTransactionStore.SaveAsync(dbContext, tx, userName);

            await HrmsDatabase.ExecuteAsync(dbContext,
                "UPDATE EmployeeLoanInstallments SET IsPosted = 1, PostedTransactionId = @Tx, PostedAt = SYSUTCDATETIME() WHERE Id = @Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Tx", txId);
                    HrmsDatabase.AddParameter(command, "@Id", d.InstId);
                });
            posted++;

            // إغلاق القرض عند تسديد كل أقساطه.
            var left = await HrmsDatabase.ScalarAsync<int>(dbContext,
                "SELECT COUNT(1) FROM EmployeeLoanInstallments WHERE LoanId = @L AND IsPosted = 0;",
                command => HrmsDatabase.AddParameter(command, "@L", d.LoanId));
            if (left == 0)
                await HrmsDatabase.ExecuteAsync(dbContext,
                    "UPDATE EmployeeLoans SET Status = N'Closed' WHERE Id = @L;",
                    command => HrmsDatabase.AddParameter(command, "@L", d.LoanId));
        }
        return posted;
    }

    private sealed class DueRow
    {
        public int InstId { get; set; }
        public int LoanId { get; set; }
        public int SeqNo { get; set; }
        public decimal Amount { get; set; }
        public int EmployeeId { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public string LoanType { get; set; } = Loan;
        public int InstallmentCount { get; set; }
    }

    private static (int Year, int Month) AddMonths(int year, int month, int add)
    {
        var zeroBased = (year * 12 + (month - 1)) + add;
        return (zeroBased / 12, zeroBased % 12 + 1);
    }

    private static async Task<string> GenerateReferenceNoAsync(ApplicationDbContext dbContext)
    {
        var prefix = $"LN{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(dbContext,
            "SELECT COUNT(1) FROM EmployeeLoans WHERE ReferenceNo LIKE @P;",
            command => HrmsDatabase.AddParameter(command, "@P", prefix + "%"));
        return $"{prefix}{count + 1:0000}";
    }

    private const string InsertSql = """
INSERT INTO EmployeeLoans
 (EmployeeId, LoanType, Amount, InstallmentCount, MonthlyAmount, StartYear, StartMonth, Reason, Note, Status, AttachmentName, AttachmentPath, ReferenceNo, CreatedBy)
VALUES
 (@Emp, @Type, @Amount, @Count, @Monthly, @SYear, @SMonth, @Reason, @Note, @Status, @AttName, @AttPath, @Ref, @By);
""";

    private const string UpdateSql = """
UPDATE EmployeeLoans SET
  EmployeeId=@Emp, LoanType=@Type, Amount=@Amount, InstallmentCount=@Count, MonthlyAmount=@Monthly,
  StartYear=@SYear, StartMonth=@SMonth, Reason=@Reason, Note=@Note, Status=@Status,
  AttachmentName=@AttName, AttachmentPath=@AttPath
WHERE Id=@Id AND Status IN (N'Pending', N'Approved');
""";

    private static Loan_ Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        LoanType = HrmsDatabase.GetString(reader, "LoanType") is { Length: > 0 } t ? t : Loan,
        Amount = reader["Amount"] is decimal am ? am : 0,
        InstallmentCount = HrmsDatabase.GetInt(reader, "InstallmentCount"),
        MonthlyAmount = reader["MonthlyAmount"] is decimal mo ? mo : 0,
        StartYear = HrmsDatabase.GetInt(reader, "StartYear"),
        StartMonth = HrmsDatabase.GetInt(reader, "StartMonth"),
        Reason = HrmsDatabase.GetString(reader, "Reason") is { Length: > 0 } rs ? rs : null,
        Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : Pending,
        AttachmentName = HrmsDatabase.GetString(reader, "AttachmentName") is { Length: > 0 } an ? an : null,
        AttachmentPath = HrmsDatabase.GetString(reader, "AttachmentPath") is { Length: > 0 } ap ? ap : null,
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default,
        PaidAmount = reader["PaidAmount"] is decimal pa ? pa : 0,
        PaidCount = HrmsDatabase.GetInt(reader, "PaidCount")
    };

    private static void Add(System.Data.Common.DbCommand command, Loan_ l)
    {
        HrmsDatabase.AddParameter(command, "@Emp", l.EmployeeId);
        HrmsDatabase.AddParameter(command, "@Type", string.IsNullOrWhiteSpace(l.LoanType) ? Loan : l.LoanType);
        HrmsDatabase.AddParameter(command, "@Amount", l.Amount);
        HrmsDatabase.AddParameter(command, "@Count", l.InstallmentCount);
        HrmsDatabase.AddParameter(command, "@Monthly", l.MonthlyAmount);
        HrmsDatabase.AddParameter(command, "@SYear", l.StartYear);
        HrmsDatabase.AddParameter(command, "@SMonth", l.StartMonth);
        HrmsDatabase.AddParameter(command, "@Reason", (object?)l.Reason ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Note", (object?)l.Note ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Status", string.IsNullOrWhiteSpace(l.Status) ? Pending : l.Status);
        HrmsDatabase.AddParameter(command, "@AttName", (object?)l.AttachmentName ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@AttPath", (object?)l.AttachmentPath ?? DBNull.Value);
    }
}
