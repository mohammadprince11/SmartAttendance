using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// زيادات الراتب (مطابقة كيان «زيادة الراتب») — تغيير دائم للراتب الأساسي للموظف
/// بأثر تاريخي. تختلف عن حركات المسير: عند «التطبيق» تُحدِّث
/// EmployeeFinancialInfos.BasicSalary مباشرةً وتحتفظ بلقطة الأساسي القديم للتأريخ.
/// نمط self-healing (CREATE + ALTER ADD idempotent).
/// </summary>
public static class SalaryRaiseStore
{
    public const string ByAmount = "Amount";
    public const string ByPercentage = "Percentage";

    public static string TypeLabel(string t) => t == ByPercentage ? "نسبة مئوية" : "مبلغ";

    public sealed class Raise
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal OldBasic { get; set; }
        public string RaiseType { get; set; } = ByAmount;
        public decimal RaiseValue { get; set; }
        public decimal NewBasic { get; set; }
        public DateOnly? EffectiveDate { get; set; }
        public string? Reason { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = "Approved";
        public bool IsApplied { get; set; }
        public DateTime? AppliedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public decimal Increase => NewBasic - OldBasic;
        public string TypeText => TypeLabel(RaiseType);
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeSalaryRaises', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeSalaryRaises
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo nvarchar(40) NULL,
        EmployeeId int NOT NULL,
        OldBasic decimal(18,2) NOT NULL DEFAULT(0),
        RaiseType nvarchar(20) NOT NULL DEFAULT(N'Amount'),
        RaiseValue decimal(18,4) NOT NULL DEFAULT(0),
        NewBasic decimal(18,2) NOT NULL DEFAULT(0),
        EffectiveDate date NULL,
        Reason nvarchar(200) NULL,
        Note nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL DEFAULT(N'Approved'),
        IsApplied bit NOT NULL DEFAULT(0),
        AppliedAt datetime2 NULL,
        AppliedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL
    );
    CREATE INDEX IX_EmployeeSalaryRaises_Employee ON EmployeeSalaryRaises (EmployeeId);
END;
""");
    }

    public sealed class EmployeeBasic
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Basic { get; set; }
    }

    /// <summary>الموظفون النشطون مع راتبهم الأساسي الحالي (لمنتقي النموذج والمعاينة).</summary>
    public static async Task<List<EmployeeBasic>> EmployeeBasicsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(f.BasicSalary, 0) AS BasicSalary
FROM Employees e
LEFT JOIN EmployeeFinancialInfos f ON f.EmployeeId = e.Id AND ISNULL(f.IsDeleted,0) = 0
WHERE ISNULL(e.IsDeleted,0) = 0 AND ISNULL(e.IsActive,1) = 1
ORDER BY e.FullName;
""",
            command => { },
            reader => new EmployeeBasic
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName"),
                Basic = reader["BasicSalary"] is decimal b ? b : 0
            });
    }

    public static async Task<List<Raise>> ListAsync(
        ApplicationDbContext dbContext, int? employeeId = null, string? status = null, bool? applied = null, string? search = null)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName
FROM EmployeeSalaryRaises r
INNER JOIN Employees e ON e.Id = r.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
ORDER BY r.CreatedAt DESC;
""",
            command => { },
            Read);

        if (employeeId is > 0) rows = rows.Where(r => r.EmployeeId == employeeId).ToList();
        if (applied.HasValue) rows = rows.Where(r => r.IsApplied == applied.Value).ToList();
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(r => r.Status == status).ToList();
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

    public static async Task<bool> IsAppliedAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        var v = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT CAST(ISNULL(IsApplied,0) AS int) FROM EmployeeSalaryRaises WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        return v == 1;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, Raise raise, string userName)
    {
        await EnsureAsync(dbContext);
        if (raise.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(dbContext, UpdateSql, command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", raise.Id);
                Add(command, raise);
            });
            return raise.Id;
        }

        if (string.IsNullOrWhiteSpace(raise.ReferenceNo))
            raise.ReferenceNo = await GenerateReferenceNoAsync(dbContext);

        return await HrmsDatabase.ScalarAsync<int>(dbContext, InsertSql + " SELECT CAST(SCOPE_IDENTITY() AS int);", command =>
        {
            Add(command, raise);
            HrmsDatabase.AddParameter(command, "@Ref", raise.ReferenceNo);
            HrmsDatabase.AddParameter(command, "@By", userName);
        });
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(dbContext,
            "DELETE FROM EmployeeSalaryRaises WHERE Id = @Id AND ISNULL(IsApplied,0) = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>
    /// تطبيق الزيادة: تحدّث الراتب الأساسي للموظف بـ NewBasic (تُنشئ سجل المعلومات
    /// المالية إن لم يوجد)، وتعلّم الزيادة كمُطبَّقة. لا يُعاد تطبيقها.
    /// </summary>
    public static async Task<bool> ApplyAsync(ApplicationDbContext dbContext, int id, string userName)
    {
        await EnsureAsync(dbContext);
        var raise = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT r.*, N'' AS EmployeeNo, N'' AS FullName, N'' AS DepartmentName FROM EmployeeSalaryRaises r WHERE r.Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            Read)).FirstOrDefault();
        if (raise is null || raise.IsApplied) return false;

        await using var tx = await dbContext.Database.BeginTransactionAsync();

        // تحديث الأساسي (upsert سجل المعلومات المالية)
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF EXISTS (SELECT 1 FROM EmployeeFinancialInfos WHERE EmployeeId = @Emp AND ISNULL(IsDeleted,0) = 0)
    UPDATE EmployeeFinancialInfos SET BasicSalary = @New, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @By
    WHERE EmployeeId = @Emp AND ISNULL(IsDeleted,0) = 0;
ELSE
    INSERT INTO EmployeeFinancialInfos (EmployeeId, BasicSalary, CreatedBy) VALUES (@Emp, @New, @By);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", raise.EmployeeId);
                HrmsDatabase.AddParameter(command, "@New", raise.NewBasic);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE EmployeeSalaryRaises SET IsApplied = 1, AppliedAt = SYSUTCDATETIME(), AppliedBy = @By, Status = N'Approved' WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });

        await tx.CommitAsync();
        return true;
    }

    /// <summary>تطبيق جماعي (= «قفل») لعدة زيادات قيد الانتظار. يرجع عدد ما طُبّق فعلاً.</summary>
    public static async Task<int> ApplyManyAsync(ApplicationDbContext dbContext, IEnumerable<int> ids, string userName)
    {
        int n = 0;
        foreach (var id in ids.Distinct())
            if (await ApplyAsync(dbContext, id, userName)) n++;
        return n;
    }

    /// <summary>حذف جماعي للزيادات غير المُطبَّقة المحددة.</summary>
    public static async Task DeleteManyAsync(ApplicationDbContext dbContext, IEnumerable<int> ids)
    {
        foreach (var id in ids.Distinct())
            await DeleteAsync(dbContext, id);
    }

    private static async Task<string> GenerateReferenceNoAsync(ApplicationDbContext dbContext)
    {
        var prefix = $"RS{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(dbContext,
            "SELECT COUNT(1) FROM EmployeeSalaryRaises WHERE ReferenceNo LIKE @P;",
            command => HrmsDatabase.AddParameter(command, "@P", prefix + "%"));
        return $"{prefix}{count + 1:0000}";
    }

    private const string InsertSql = """
INSERT INTO EmployeeSalaryRaises
 (EmployeeId, OldBasic, RaiseType, RaiseValue, NewBasic, EffectiveDate, Reason, Note, Status, ReferenceNo, CreatedBy)
VALUES
 (@Emp, @Old, @Type, @Value, @New, @Eff, @Reason, @Note, @Status, @Ref, @By);
""";

    private const string UpdateSql = """
UPDATE EmployeeSalaryRaises SET
  EmployeeId=@Emp, OldBasic=@Old, RaiseType=@Type, RaiseValue=@Value, NewBasic=@New,
  EffectiveDate=@Eff, Reason=@Reason, Note=@Note, Status=@Status
WHERE Id=@Id AND ISNULL(IsApplied,0) = 0;
""";

    private static Raise Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        OldBasic = reader["OldBasic"] is decimal ob ? ob : 0,
        RaiseType = HrmsDatabase.GetString(reader, "RaiseType") is { Length: > 0 } t ? t : ByAmount,
        RaiseValue = reader["RaiseValue"] is decimal rv ? rv : 0,
        NewBasic = reader["NewBasic"] is decimal nb ? nb : 0,
        EffectiveDate = HrmsDatabase.GetDateOnly(reader, "EffectiveDate"),
        Reason = HrmsDatabase.GetString(reader, "Reason") is { Length: > 0 } rs ? rs : null,
        Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : "Approved",
        IsApplied = HrmsDatabase.GetBool(reader, "IsApplied"),
        AppliedAt = HrmsDatabase.GetDateTime(reader, "AppliedAt"),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
    };

    private static void Add(System.Data.Common.DbCommand command, Raise r)
    {
        object? D(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        HrmsDatabase.AddParameter(command, "@Emp", r.EmployeeId);
        HrmsDatabase.AddParameter(command, "@Old", r.OldBasic);
        HrmsDatabase.AddParameter(command, "@Type", r.RaiseType);
        HrmsDatabase.AddParameter(command, "@Value", r.RaiseValue);
        HrmsDatabase.AddParameter(command, "@New", r.NewBasic);
        HrmsDatabase.AddParameter(command, "@Eff", D(r.EffectiveDate));
        HrmsDatabase.AddParameter(command, "@Reason", (object?)r.Reason ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Note", (object?)r.Note ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Status", string.IsNullOrWhiteSpace(r.Status) ? "Approved" : r.Status);
    }
}
