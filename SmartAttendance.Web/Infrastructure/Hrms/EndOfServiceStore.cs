using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تسويات نهاية الخدمة (مطابقة كيان «نهاية الخدمة / STB») — تحسب مكافأة نهاية الخدمة
/// بشرائح سنوات الخدمة (EndOfServiceRule.Tiers) على آخر راتب أساسي، مضافاً إليها بدل
/// رصيد الإجازات ومستحقات أخرى، ناقصاً الاقتطاعات = صافي التسوية. نمط self-healing.
/// ⚠️ الشرائح الافتراضية تقريبية «تحتاج تأكيد محاسب/قانون العمل العراقي».
/// </summary>
public static class EndOfServiceStore
{
    /// <summary>شرائح المكافأة الافتراضية: أشهر لكل سنة خدمة ضمن مدى السنوات (نمط EndOfServiceRule.Tiers).</summary>
    private static readonly (decimal From, decimal To, decimal MonthsPerYear)[] DefaultTiers =
    {
        (0m, 5m, 0.5m),      // أول 5 سنوات: نصف شهر عن كل سنة
        (5m, 9999m, 1.0m),   // ما بعد 5 سنوات: شهر عن كل سنة
    };

    /// <summary>مكافأة نهاية الخدمة بالشرائح على آخر أساسي شهري + وصف نصّي للتفصيل.</summary>
    public static (decimal Gratuity, string Breakdown) ComputeGratuity(decimal years, decimal monthlyBasic)
    {
        decimal total = 0;
        var parts = new List<string>();
        foreach (var t in DefaultTiers)
        {
            if (years <= t.From) continue;
            var yearsInTier = Math.Min(years, t.To) - t.From;
            if (yearsInTier <= 0) continue;
            var amt = Math.Round(yearsInTier * t.MonthsPerYear * monthlyBasic, 2);
            total += amt;
            parts.Add($"{yearsInTier:0.##}س × {t.MonthsPerYear:0.##} شهر");
        }
        return (Math.Round(total, 2), parts.Count > 0 ? string.Join(" + ", parts) : "—");
    }

    public static decimal YearsOfService(DateOnly start, DateOnly end)
        => end <= start ? 0 : Math.Round((decimal)(end.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25m, 2);

    public sealed class Settlement
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateOnly? ServiceStartDate { get; set; }
        public DateOnly? LastWorkingDate { get; set; }
        public decimal YearsService { get; set; }
        public decimal LastBasic { get; set; }
        public string? Reason { get; set; }
        public decimal GratuityAmount { get; set; }
        public decimal LeaveBalanceDays { get; set; }
        public decimal LeaveEncashment { get; set; }
        public decimal OtherDues { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetSettlement { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = "Draft";
        public DateTime? ApprovedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsApproved => Status == "Approved";
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeEndOfService', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeEndOfService
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo nvarchar(40) NULL,
        EmployeeId int NOT NULL,
        ServiceStartDate date NULL,
        LastWorkingDate date NULL,
        YearsService decimal(6,2) NOT NULL DEFAULT(0),
        LastBasic decimal(18,2) NOT NULL DEFAULT(0),
        Reason nvarchar(200) NULL,
        GratuityAmount decimal(18,2) NOT NULL DEFAULT(0),
        LeaveBalanceDays decimal(9,2) NOT NULL DEFAULT(0),
        LeaveEncashment decimal(18,2) NOT NULL DEFAULT(0),
        OtherDues decimal(18,2) NOT NULL DEFAULT(0),
        Deductions decimal(18,2) NOT NULL DEFAULT(0),
        NetSettlement decimal(18,2) NOT NULL DEFAULT(0),
        Note nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL DEFAULT(N'Draft'),
        ApprovedAt datetime2 NULL,
        ApprovedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL
    );
    CREATE INDEX IX_EmployeeEndOfService_Employee ON EmployeeEndOfService (EmployeeId);
END;
""");
    }

    public sealed class EmployeeInfo
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Basic { get; set; }
        public DateOnly? HireDate { get; set; }
    }

    /// <summary>الموظفون مع الأساسي الحالي وتاريخ التعيين (لمنتقي النموذج والافتراضات).</summary>
    public static async Task<List<EmployeeInfo>> EmployeeInfosAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(f.BasicSalary, 0) AS BasicSalary, COALESCE(e.HireDate, e.JoiningDate) AS HireDate
FROM Employees e
LEFT JOIN EmployeeFinancialInfos f ON f.EmployeeId = e.Id AND ISNULL(f.IsDeleted,0) = 0
WHERE ISNULL(e.IsDeleted,0) = 0
ORDER BY e.FullName;
""",
            command => { },
            reader => new EmployeeInfo
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName"),
                Basic = reader["BasicSalary"] is decimal b ? b : 0,
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate")
            });
    }

    public static async Task<List<Settlement>> ListAsync(
        ApplicationDbContext dbContext, string? status = null, string? search = null)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT s.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName
FROM EmployeeEndOfService s
INNER JOIN Employees e ON e.Id = s.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
ORDER BY s.CreatedAt DESC;
""",
            command => { },
            Read);

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

    public static async Task<bool> IsApprovedAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        var v = await HrmsDatabase.ScalarAsync<string>(
            dbContext,
            "SELECT ISNULL(Status, N'Draft') FROM EmployeeEndOfService WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        return v == "Approved";
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, Settlement s, string userName)
    {
        await EnsureAsync(dbContext);
        if (s.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(dbContext, UpdateSql, command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", s.Id);
                Add(command, s);
            });
            return s.Id;
        }

        if (string.IsNullOrWhiteSpace(s.ReferenceNo))
            s.ReferenceNo = await GenerateReferenceNoAsync(dbContext);

        return await HrmsDatabase.ScalarAsync<int>(dbContext, InsertSql + " SELECT CAST(SCOPE_IDENTITY() AS int);", command =>
        {
            Add(command, s);
            HrmsDatabase.AddParameter(command, "@Ref", s.ReferenceNo);
            HrmsDatabase.AddParameter(command, "@By", userName);
        });
    }

    public static async Task<bool> ApproveAsync(ApplicationDbContext dbContext, int id, string userName)
    {
        await EnsureAsync(dbContext);
        if (await IsApprovedAsync(dbContext, id)) return false;
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE EmployeeEndOfService SET Status = N'Approved', ApprovedAt = SYSUTCDATETIME(), ApprovedBy = @By WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return true;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(dbContext,
            "DELETE FROM EmployeeEndOfService WHERE Id = @Id AND ISNULL(Status, N'Draft') <> N'Approved';",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static async Task<string> GenerateReferenceNoAsync(ApplicationDbContext dbContext)
    {
        var prefix = $"EOS{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(dbContext,
            "SELECT COUNT(1) FROM EmployeeEndOfService WHERE ReferenceNo LIKE @P;",
            command => HrmsDatabase.AddParameter(command, "@P", prefix + "%"));
        return $"{prefix}{count + 1:0000}";
    }

    private const string InsertSql = """
INSERT INTO EmployeeEndOfService
 (EmployeeId, ServiceStartDate, LastWorkingDate, YearsService, LastBasic, Reason, GratuityAmount,
  LeaveBalanceDays, LeaveEncashment, OtherDues, Deductions, NetSettlement, Note, Status, ReferenceNo, CreatedBy)
VALUES
 (@Emp, @Start, @End, @Years, @Basic, @Reason, @Gratuity,
  @LeaveDays, @LeaveEnc, @OtherDues, @Deductions, @Net, @Note, @Status, @Ref, @By);
""";

    private const string UpdateSql = """
UPDATE EmployeeEndOfService SET
  EmployeeId=@Emp, ServiceStartDate=@Start, LastWorkingDate=@End, YearsService=@Years, LastBasic=@Basic,
  Reason=@Reason, GratuityAmount=@Gratuity, LeaveBalanceDays=@LeaveDays, LeaveEncashment=@LeaveEnc,
  OtherDues=@OtherDues, Deductions=@Deductions, NetSettlement=@Net, Note=@Note, Status=@Status
WHERE Id=@Id AND ISNULL(Status, N'Draft') <> N'Approved';
""";

    private static Settlement Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        ServiceStartDate = HrmsDatabase.GetDateOnly(reader, "ServiceStartDate"),
        LastWorkingDate = HrmsDatabase.GetDateOnly(reader, "LastWorkingDate"),
        YearsService = reader["YearsService"] is decimal ys ? ys : 0,
        LastBasic = reader["LastBasic"] is decimal lb ? lb : 0,
        Reason = HrmsDatabase.GetString(reader, "Reason") is { Length: > 0 } rs ? rs : null,
        GratuityAmount = reader["GratuityAmount"] is decimal ga ? ga : 0,
        LeaveBalanceDays = reader["LeaveBalanceDays"] is decimal ld ? ld : 0,
        LeaveEncashment = reader["LeaveEncashment"] is decimal le ? le : 0,
        OtherDues = reader["OtherDues"] is decimal od ? od : 0,
        Deductions = reader["Deductions"] is decimal dd ? dd : 0,
        NetSettlement = reader["NetSettlement"] is decimal ns ? ns : 0,
        Note = HrmsDatabase.GetString(reader, "Note") is { Length: > 0 } n ? n : null,
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : "Draft",
        ApprovedAt = HrmsDatabase.GetDateTime(reader, "ApprovedAt"),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
    };

    private static void Add(System.Data.Common.DbCommand command, Settlement s)
    {
        object? D(DateOnly? d) => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        HrmsDatabase.AddParameter(command, "@Emp", s.EmployeeId);
        HrmsDatabase.AddParameter(command, "@Start", D(s.ServiceStartDate));
        HrmsDatabase.AddParameter(command, "@End", D(s.LastWorkingDate));
        HrmsDatabase.AddParameter(command, "@Years", s.YearsService);
        HrmsDatabase.AddParameter(command, "@Basic", s.LastBasic);
        HrmsDatabase.AddParameter(command, "@Reason", (object?)s.Reason ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Gratuity", s.GratuityAmount);
        HrmsDatabase.AddParameter(command, "@LeaveDays", s.LeaveBalanceDays);
        HrmsDatabase.AddParameter(command, "@LeaveEnc", s.LeaveEncashment);
        HrmsDatabase.AddParameter(command, "@OtherDues", s.OtherDues);
        HrmsDatabase.AddParameter(command, "@Deductions", s.Deductions);
        HrmsDatabase.AddParameter(command, "@Net", s.NetSettlement);
        HrmsDatabase.AddParameter(command, "@Note", (object?)s.Note ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Status", string.IsNullOrWhiteSpace(s.Status) ? "Draft" : s.Status);
    }
}
