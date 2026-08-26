using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تهيئة المسير: ملفات الضريبة (شرائح تراكمية + إعفاء) وملفات الضمان الاجتماعي
/// (نسبة موظف/شركة + سقف) — نمط كيان «تصنيفات الضريبة/الضمان» المفحوص حياً.
/// كل شي config قابل للتعديل من /Payroll/Settings؛ القيم المبذورة عراقية مبدئية
/// (GOSI موظف 5% / شركة 12% + شرائح تصاعدية) وعليها علم «تحتاج تأكيد محاسب».
/// EOS (مكافأة نهاية الخدمة) مؤجل لدورة الإنهاء لا للمسير الشهري.
/// </summary>
public static class PayrollConfigStore
{
    public sealed class TaxBracket
    {
        public int Id { get; set; }
        public decimal FromAmount { get; set; }
        public decimal? ToAmount { get; set; }        // null = فما فوق
        public decimal Rate { get; set; }             // نسبة مئوية
    }

    public sealed class TaxProfile
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal ExemptionAmount { get; set; }  // إعفاء شهري يُطرح قبل الشرائح
        public bool IsActive { get; set; } = true;

        /// <summary>شروط انطباق الملف تلقائياً (محرّك الشروط العام)؛ فارغة ⟹ يُسنَد يدوياً فقط.</summary>
        public string ConditionsJson { get; set; } = string.Empty;

        /// <summary>أسبقية الملفات المشروطة — الأصغر يُفحص أولاً.</summary>
        public int SortOrder { get; set; }

        public List<TaxBracket> Brackets { get; set; } = new();
    }

    public sealed class GosiProfile
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal EmployeeRate { get; set; }
        public decimal CompanyRate { get; set; }
        public decimal Ceiling { get; set; }          // 0 = بلا سقف
        public bool IsActive { get; set; } = true;

        /// <summary>شروط انطباق الملف تلقائياً — «هذا الملف للمواطنين» مثلاً.</summary>
        public string ConditionsJson { get; set; } = string.Empty;

        public int SortOrder { get; set; }
    }

    /// <summary>مرشَّحو الحسم لملفات الضريبة بترتيب القراءة نفسه.</summary>
    public static List<PayrollProfileResolver.Candidate> Candidates(IEnumerable<TaxProfile> profiles) =>
        profiles
            .Select(profile => new PayrollProfileResolver.Candidate(
                profile.Id, profile.Name, profile.SortOrder, profile.IsActive,
                HrConditions.Deserialize(profile.ConditionsJson)))
            .ToList();

    /// <summary>مرشَّحو الحسم لملفات الضمان بترتيب القراءة نفسه.</summary>
    public static List<PayrollProfileResolver.Candidate> Candidates(IEnumerable<GosiProfile> profiles) =>
        profiles
            .Select(profile => new PayrollProfileResolver.Candidate(
                profile.Id, profile.Name, profile.SortOrder, profile.IsActive,
                HrConditions.Deserialize(profile.ConditionsJson)))
            .ToList();

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PayrollTaxProfiles', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollTaxProfiles
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NULL,
        Name nvarchar(150) NOT NULL,
        ExemptionAmount decimal(18,2) NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        ConditionsJson nvarchar(max) NULL,
        SortOrder int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('PayrollTaxBrackets', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollTaxBrackets
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NULL,
        ProfileId int NOT NULL,
        FromAmount decimal(18,2) NOT NULL DEFAULT(0),
        ToAmount decimal(18,2) NULL,
        Rate decimal(9,4) NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_PayrollTaxBrackets_Profile ON PayrollTaxBrackets (ProfileId, FromAmount);
END;

IF OBJECT_ID('PayrollGosiProfiles', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollGosiProfiles
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NULL,
        Name nvarchar(150) NOT NULL,
        EmployeeRate decimal(9,4) NOT NULL DEFAULT(0),
        CompanyRate decimal(9,4) NOT NULL DEFAULT(0),
        Ceiling decimal(18,2) NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        ConditionsJson nvarchar(max) NULL,
        SortOrder int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- بذور عراقية مبدئية (تحتاج تأكيد محاسب) — تُبذر مرة واحدة فقط
IF NOT EXISTS (SELECT 1 FROM PayrollGosiProfiles)
    INSERT INTO PayrollGosiProfiles (Name, EmployeeRate, CompanyRate, Ceiling)
    VALUES (N'الضمان الاجتماعي — العراق (مبدئي، تحتاج تأكيد محاسب)', 5, 12, 0);

IF NOT EXISTS (SELECT 1 FROM PayrollTaxProfiles)
BEGIN
    INSERT INTO PayrollTaxProfiles (Name, ExemptionAmount) VALUES (N'ضريبة الدخل — العراق (مبدئي، تحتاج تأكيد محاسب)', 250000);
    DECLARE @pid int = SCOPE_IDENTITY();
    INSERT INTO PayrollTaxBrackets (ProfileId, FromAmount, ToAmount, Rate) VALUES
      (@pid, 0,       250000, 3),
      (@pid, 250000,  500000, 5),
      (@pid, 500000, 1000000, 10),
      (@pid, 1000000, NULL,   15);
END;
""");
    }

    // ---------------- الضريبة ----------------
    public static async Task<List<TaxProfile>> ListTaxProfilesAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int? companyId = null)
    {
        await EnsureAsync(dbContext);
        var companyPredicate = ProfilePredicate(scope, companyId, "CompanyId");
        var profiles = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT * FROM PayrollTaxProfiles WHERE {companyPredicate} ORDER BY CompanyId DESC, Name;",
            command => { },
            reader => new TaxProfile
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetNullableInt(reader, "CompanyId"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                ExemptionAmount = reader["ExemptionAmount"] is decimal e ? e : 0,
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                ConditionsJson = HrmsDatabase.GetString(reader, "ConditionsJson") ?? string.Empty,
                SortOrder = HrmsDatabase.GetNullableInt(reader, "SortOrder") ?? 0
            });

        var brackets = await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT b.* FROM PayrollTaxBrackets b
INNER JOIN PayrollTaxProfiles p ON p.Id = b.ProfileId
WHERE {ProfilePredicate(scope, companyId, "p.CompanyId")}
ORDER BY b.ProfileId, b.FromAmount;
""",
            command => { },
            reader => new
            {
                ProfileId = HrmsDatabase.GetInt(reader, "ProfileId"),
                Bracket = new TaxBracket
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    FromAmount = reader["FromAmount"] is decimal f ? f : 0,
                    ToAmount = reader["ToAmount"] is decimal t ? t : null,
                    Rate = reader["Rate"] is decimal r ? r : 0
                }
            });

        var byProfile = brackets.GroupBy(x => x.ProfileId).ToDictionary(g => g.Key, g => g.Select(x => x.Bracket).ToList());
        foreach (var p in profiles)
            p.Brackets = byProfile.TryGetValue(p.Id, out var list) ? list : new();
        return profiles;
    }

    public static async Task<TaxProfile?> ActiveTaxProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId) =>
        (await ListTaxProfilesAsync(dbContext, scope, companyId)).FirstOrDefault(x => x.IsActive)
        ?? (await ListTaxProfilesAsync(dbContext, scope, companyId)).FirstOrDefault();

    /// <summary>يحفظ ملف الضريبة وشرائحه ويعيد معرّفه — يحتاجه المستدعي لربط وعاء الاحتساب.</summary>
    public static async Task<int> SaveTaxProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, TaxProfile profile)
    {
        await EnsureAsync(dbContext);
        if (profile.CompanyId is not > 0 || !scope.Allows(profile.CompanyId))
            throw new UnauthorizedAccessException("Tax profile company is outside the effective scope.");
        var companyPredicate = scope.ToSqlPredicate("CompanyId");
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        int id;
        if (profile.Id > 0)
        {
            id = profile.Id;
            var authorized = await HrmsDatabase.ScalarAsync<int>(dbContext,
                $"SELECT COUNT(*) FROM PayrollTaxProfiles WHERE Id=@Id AND {companyPredicate};",
                command => HrmsDatabase.AddParameter(command, "@Id", id));
            if (authorized == 0) throw new UnauthorizedAccessException("Tax profile is outside the effective scope.");
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"""
UPDATE PayrollTaxProfiles SET CompanyId=@CompanyId, Name=@Name, ExemptionAmount=@Exemption, IsActive=@Active, ConditionsJson=@Conditions, SortOrder=@Sort
WHERE Id=@Id AND {companyPredicate};
DELETE FROM PayrollTaxBrackets WHERE ProfileId=@Id AND EXISTS
    (SELECT 1 FROM PayrollTaxProfiles p WHERE p.Id=@Id AND {scope.ToSqlPredicate("p.CompanyId")});
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    AddTax(command, profile);
                });
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                "INSERT INTO PayrollTaxProfiles (CompanyId, Name, ExemptionAmount, IsActive, ConditionsJson, SortOrder) VALUES (@CompanyId, @Name, @Exemption, @Active, @Conditions, @Sort); SELECT CAST(SCOPE_IDENTITY() AS int);",
                command => AddTax(command, profile));
        }

        foreach (var b in profile.Brackets.OrderBy(x => x.FromAmount))
        {
            var current = b;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO PayrollTaxBrackets (ProfileId, FromAmount, ToAmount, Rate) VALUES (@Pid, @From, @To, @Rate);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Pid", id);
                    HrmsDatabase.AddParameter(command, "@From", current.FromAmount);
                    HrmsDatabase.AddParameter(command, "@To", (object?)current.ToAmount ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@Rate", current.Rate);
                });
        }

        await transaction.CommitAsync();
        return id;
    }

    public static async Task DeleteTaxProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
DELETE FROM PayrollTaxBrackets WHERE ProfileId=@Id AND EXISTS
    (SELECT 1 FROM PayrollTaxProfiles p WHERE p.Id=@Id AND {scope.ToSqlPredicate("p.CompanyId")});
DELETE FROM PayrollTaxProfiles WHERE Id=@Id AND {scope.ToSqlPredicate("CompanyId")};
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>حساب الضريبة التصاعدية على الوعاء الخاضع (بعد الإعفاء) — تراكمي بالشرائح.</summary>
    public static decimal ComputeTax(decimal taxable, TaxProfile? profile)
    {
        if (profile == null) return 0;
        var basis = Math.Max(0, taxable - profile.ExemptionAmount);
        if (basis <= 0 || profile.Brackets.Count == 0) return 0;

        decimal tax = 0;
        foreach (var b in profile.Brackets.OrderBy(x => x.FromAmount))
        {
            if (basis <= b.FromAmount) break;
            var upper = b.ToAmount ?? basis;
            var slice = Math.Min(basis, upper) - b.FromAmount;
            if (slice > 0) tax += slice * b.Rate / 100m;
        }
        return Math.Round(tax, 2);
    }

    // ---------------- الضمان الاجتماعي ----------------
    public static async Task<List<GosiProfile>> ListGosiProfilesAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int? companyId = null)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT * FROM PayrollGosiProfiles WHERE {ProfilePredicate(scope, companyId, "CompanyId")} ORDER BY CompanyId DESC, Name;",
            command => { },
            reader => new GosiProfile
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetNullableInt(reader, "CompanyId"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                EmployeeRate = reader["EmployeeRate"] is decimal er ? er : 0,
                CompanyRate = reader["CompanyRate"] is decimal cr ? cr : 0,
                Ceiling = reader["Ceiling"] is decimal c ? c : 0,
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                ConditionsJson = HrmsDatabase.GetString(reader, "ConditionsJson") ?? string.Empty,
                SortOrder = HrmsDatabase.GetNullableInt(reader, "SortOrder") ?? 0
            });
    }

    public static async Task<GosiProfile?> ActiveGosiProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId) =>
        (await ListGosiProfilesAsync(dbContext, scope, companyId)).FirstOrDefault(x => x.IsActive)
        ?? (await ListGosiProfilesAsync(dbContext, scope, companyId)).FirstOrDefault();

    /// <summary>يحفظ ملف الضمان ويعيد معرّفه — يحتاجه المستدعي لربط وعاء الاحتساب.</summary>
    public static async Task<int> SaveGosiProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, GosiProfile profile)
    {
        await EnsureAsync(dbContext);
        if (profile.CompanyId is not > 0 || !scope.Allows(profile.CompanyId))
            throw new UnauthorizedAccessException("GOSI profile company is outside the effective scope.");
        if (profile.Id > 0)
        {
            var authorized = await HrmsDatabase.ScalarAsync<int>(dbContext,
                $"SELECT COUNT(*) FROM PayrollGosiProfiles WHERE Id=@Id AND {scope.ToSqlPredicate("CompanyId")};",
                command => HrmsDatabase.AddParameter(command, "@Id", profile.Id));
            if (authorized == 0) throw new UnauthorizedAccessException("GOSI profile is outside the effective scope.");
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"UPDATE PayrollGosiProfiles SET CompanyId=@CompanyId, Name=@Name, EmployeeRate=@Emp, CompanyRate=@Co, Ceiling=@Ceiling, IsActive=@Active, ConditionsJson=@Conditions, SortOrder=@Sort WHERE Id=@Id AND {scope.ToSqlPredicate("CompanyId")};",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", profile.Id);
                    AddGosi(command, profile);
                });

            return profile.Id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "INSERT INTO PayrollGosiProfiles (CompanyId, Name, EmployeeRate, CompanyRate, Ceiling, IsActive, ConditionsJson, SortOrder) VALUES (@CompanyId, @Name, @Emp, @Co, @Ceiling, @Active, @Conditions, @Sort); SELECT CAST(SCOPE_IDENTITY() AS int);",
            command => AddGosi(command, profile));
    }

    public static async Task DeleteGosiProfileAsync(ApplicationDbContext dbContext, CompanyScope scope, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PayrollGosiProfiles WHERE Id=@Id AND " + scope.ToSqlPredicate("CompanyId") + ";",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>حصتا الموظف والشركة من الضمان على الإجمالي (مع السقف إن وُجد).</summary>
    public static (decimal Employee, decimal Company) ComputeGosi(decimal gross, GosiProfile? profile)
    {
        if (profile == null) return (0, 0);
        var basis = profile.Ceiling > 0 && gross > profile.Ceiling ? profile.Ceiling : gross;
        if (basis <= 0) return (0, 0);
        return (Math.Round(basis * profile.EmployeeRate / 100m, 2), Math.Round(basis * profile.CompanyRate / 100m, 2));
    }

    private static void AddGosi(System.Data.Common.DbCommand command, GosiProfile profile)
    {
        HrmsDatabase.AddParameter(command, "@CompanyId", profile.CompanyId!.Value);
        HrmsDatabase.AddParameter(command, "@Name", profile.Name);
        HrmsDatabase.AddParameter(command, "@Emp", profile.EmployeeRate);
        HrmsDatabase.AddParameter(command, "@Co", profile.CompanyRate);
        HrmsDatabase.AddParameter(command, "@Ceiling", profile.Ceiling);
        HrmsDatabase.AddParameter(command, "@Active", profile.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Conditions", (object?)Blank(profile.ConditionsJson) ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Sort", profile.SortOrder);
    }

    private static void AddTax(System.Data.Common.DbCommand command, TaxProfile profile)
    {
        HrmsDatabase.AddParameter(command, "@CompanyId", profile.CompanyId!.Value);
        HrmsDatabase.AddParameter(command, "@Name", profile.Name);
        HrmsDatabase.AddParameter(command, "@Exemption", profile.ExemptionAmount);
        HrmsDatabase.AddParameter(command, "@Active", profile.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Conditions", (object?)Blank(profile.ConditionsJson) ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Sort", profile.SortOrder);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ProfilePredicate(CompanyScope scope, int? companyId, string column)
    {
        if (companyId is > 0)
        {
            if (!scope.Allows(companyId)) return "1 = 0";
            return $"({column} IS NULL OR {column} = {companyId.Value})";
        }

        return scope.ToSharedConfigSqlPredicate(column);
    }
}
