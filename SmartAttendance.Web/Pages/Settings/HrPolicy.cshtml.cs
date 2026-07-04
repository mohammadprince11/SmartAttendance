using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Settings;

public class HrPolicyModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public HrPolicyModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public SocialSecurityInput Social { get; set; } = new();

    [BindProperty]
    public TaxInput Tax { get; set; } = new();

    [BindProperty]
    public List<TaxBracketInput> TaxBrackets { get; set; } = new();

    [BindProperty]
    public List<LeavePolicyInput> LeavePolicies { get; set; } = new();

    [BindProperty]
    public NoticePolicyInput Notice { get; set; } = new();

    [BindProperty]
    public List<PenaltyPolicyInput> Penalties { get; set; } = new();

    public string? SuccessMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await PrepareAsync();
    }

    public async Task<IActionResult> OnPostSaveSocialAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF EXISTS (SELECT 1 FROM HrSocialSecuritySettings WHERE Id = 1)
BEGIN
    UPDATE HrSocialSecuritySettings
    SET IsEnabled = @IsEnabled,
        EmployeeRate = @EmployeeRate,
        CompanyRate = @CompanyRate,
        MinSalary = @MinSalary,
        MaxSalary = @MaxSalary,
        ScopeType = @ScopeType,
        EffectiveFrom = @EffectiveFrom,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = 1;
END
ELSE
BEGIN
    SET IDENTITY_INSERT HrSocialSecuritySettings ON;
    INSERT INTO HrSocialSecuritySettings
    (Id, PolicyName, IsEnabled, EmployeeRate, CompanyRate, MinSalary, MaxSalary, ScopeType, EffectiveFrom, UpdatedAt, UpdatedBy)
    VALUES
    (1, 'Default Social Security Policy', @IsEnabled, @EmployeeRate, @CompanyRate, @MinSalary, @MaxSalary, @ScopeType, @EffectiveFrom, GETDATE(), @UpdatedBy);
    SET IDENTITY_INSERT HrSocialSecuritySettings OFF;
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@IsEnabled", Social.IsEnabled);
                HrmsDatabase.AddParameter(command, "@EmployeeRate", Social.EmployeeRate);
                HrmsDatabase.AddParameter(command, "@CompanyRate", Social.CompanyRate);
                HrmsDatabase.AddParameter(command, "@MinSalary", Social.MinSalary);
                HrmsDatabase.AddParameter(command, "@MaxSalary", Social.MaxSalary);
                HrmsDatabase.AddParameter(command, "@ScopeType", Social.ScopeType ?? "Company");
                HrmsDatabase.AddParameter(command, "@EffectiveFrom", ToSqlDate(Social.EffectiveFrom));
                HrmsDatabase.AddParameter(command, "@UpdatedBy", CurrentUser());
            });

        TempData["SuccessMessage"] = "تم حفظ إعدادات الضمان الاجتماعي.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveTaxAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF EXISTS (SELECT 1 FROM HrTaxSettings WHERE Id = 1)
BEGIN
    UPDATE HrTaxSettings
    SET IsEnabled = @IsEnabled,
        ExemptionAmount = @ExemptionAmount,
        CalculationMethod = @CalculationMethod,
        ScopeType = @ScopeType,
        EffectiveFrom = @EffectiveFrom,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = 1;
END
ELSE
BEGIN
    SET IDENTITY_INSERT HrTaxSettings ON;
    INSERT INTO HrTaxSettings
    (Id, PolicyName, IsEnabled, ExemptionAmount, CalculationMethod, ScopeType, EffectiveFrom, UpdatedAt, UpdatedBy)
    VALUES
    (1, 'Default Tax Policy', @IsEnabled, @ExemptionAmount, @CalculationMethod, @ScopeType, @EffectiveFrom, GETDATE(), @UpdatedBy);
    SET IDENTITY_INSERT HrTaxSettings OFF;
END;

DELETE FROM HrTaxBrackets;

INSERT INTO HrTaxBrackets (FromAmount, ToAmount, Rate, SortOrder)
VALUES
(@From1, @To1, @Rate1, 1),
(@From2, @To2, @Rate2, 2),
(@From3, @To3, @Rate3, 3);",
            command =>
            {
                var b1 = GetBracket(0);
                var b2 = GetBracket(1);
                var b3 = GetBracket(2);

                HrmsDatabase.AddParameter(command, "@IsEnabled", Tax.IsEnabled);
                HrmsDatabase.AddParameter(command, "@ExemptionAmount", Tax.ExemptionAmount);
                HrmsDatabase.AddParameter(command, "@CalculationMethod", Tax.CalculationMethod ?? "Brackets");
                HrmsDatabase.AddParameter(command, "@ScopeType", Tax.ScopeType ?? "Company");
                HrmsDatabase.AddParameter(command, "@EffectiveFrom", ToSqlDate(Tax.EffectiveFrom));
                HrmsDatabase.AddParameter(command, "@UpdatedBy", CurrentUser());

                HrmsDatabase.AddParameter(command, "@From1", b1.FromAmount);
                HrmsDatabase.AddParameter(command, "@To1", b1.ToAmount);
                HrmsDatabase.AddParameter(command, "@Rate1", b1.Rate);

                HrmsDatabase.AddParameter(command, "@From2", b2.FromAmount);
                HrmsDatabase.AddParameter(command, "@To2", b2.ToAmount);
                HrmsDatabase.AddParameter(command, "@Rate2", b2.Rate);

                HrmsDatabase.AddParameter(command, "@From3", b3.FromAmount);
                HrmsDatabase.AddParameter(command, "@To3", b3.ToAmount);
                HrmsDatabase.AddParameter(command, "@Rate3", b3.Rate);
            });

        TempData["SuccessMessage"] = "تم حفظ إعدادات الضريبة والشرائح.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveLeavesAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(_dbContext, "DELETE FROM HrLeavePolicySettings;");

        var sort = 1;
        foreach (var leave in LeavePolicies.Where(x => !string.IsNullOrWhiteSpace(x.LeaveType)))
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
INSERT INTO HrLeavePolicySettings
(LeaveType, AnnualBalance, AccrualMethod, AllowCarryForward, MaxCarryForward, IsPaid, RequiresApproval, EffectiveFrom, SortOrder)
VALUES
(@LeaveType, @AnnualBalance, @AccrualMethod, @AllowCarryForward, @MaxCarryForward, @IsPaid, @RequiresApproval, @EffectiveFrom, @SortOrder);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@LeaveType", leave.LeaveType.Trim());
                    HrmsDatabase.AddParameter(command, "@AnnualBalance", leave.AnnualBalance);
                    HrmsDatabase.AddParameter(command, "@AccrualMethod", leave.AccrualMethod ?? "Annual");
                    HrmsDatabase.AddParameter(command, "@AllowCarryForward", leave.AllowCarryForward);
                    HrmsDatabase.AddParameter(command, "@MaxCarryForward", leave.MaxCarryForward);
                    HrmsDatabase.AddParameter(command, "@IsPaid", leave.IsPaid);
                    HrmsDatabase.AddParameter(command, "@RequiresApproval", leave.RequiresApproval);
                    HrmsDatabase.AddParameter(command, "@EffectiveFrom", ToSqlDate(leave.EffectiveFrom));
                    HrmsDatabase.AddParameter(command, "@SortOrder", sort++);
                });
        }

        TempData["SuccessMessage"] = "تم حفظ إعدادات الإجازات.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveNoticeAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF EXISTS (SELECT 1 FROM HrNoticePolicySettings WHERE Id = 1)
BEGIN
    UPDATE HrNoticePolicySettings
    SET PolicyName = @PolicyName,
        NoticeDays = @NoticeDays,
        AppliesTo = @AppliesTo,
        EffectiveFrom = @EffectiveFrom,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = 1;
END
ELSE
BEGIN
    SET IDENTITY_INSERT HrNoticePolicySettings ON;
    INSERT INTO HrNoticePolicySettings
    (Id, PolicyName, NoticeDays, AppliesTo, EffectiveFrom, UpdatedAt, UpdatedBy)
    VALUES
    (1, @PolicyName, @NoticeDays, @AppliesTo, @EffectiveFrom, GETDATE(), @UpdatedBy);
    SET IDENTITY_INSERT HrNoticePolicySettings OFF;
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PolicyName", string.IsNullOrWhiteSpace(Notice.PolicyName) ? "Default Notice Policy" : Notice.PolicyName.Trim());
                HrmsDatabase.AddParameter(command, "@NoticeDays", Notice.NoticeDays);
                HrmsDatabase.AddParameter(command, "@AppliesTo", Notice.AppliesTo ?? "All Employees");
                HrmsDatabase.AddParameter(command, "@EffectiveFrom", ToSqlDate(Notice.EffectiveFrom));
                HrmsDatabase.AddParameter(command, "@UpdatedBy", CurrentUser());
            });

        TempData["SuccessMessage"] = "تم حفظ إعدادات فترة الإنذار.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSavePenaltiesAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(_dbContext, "DELETE FROM HrPenaltyPolicySettings;");

        foreach (var penalty in Penalties.Where(x => !string.IsNullOrWhiteSpace(x.PenaltyName)))
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
INSERT INTO HrPenaltyPolicySettings
(Code, PenaltyName, Category, PenaltyType, PenaltyValue, ExpiryMonths, AffectsContractRenewal, RequiresApproval, RequiresAttachment, IsActive)
VALUES
(@Code, @PenaltyName, @Category, @PenaltyType, @PenaltyValue, @ExpiryMonths, @AffectsContractRenewal, @RequiresApproval, @RequiresAttachment, @IsActive);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Code", string.IsNullOrWhiteSpace(penalty.Code) ? "-" : penalty.Code.Trim());
                    HrmsDatabase.AddParameter(command, "@PenaltyName", penalty.PenaltyName.Trim());
                    HrmsDatabase.AddParameter(command, "@Category", penalty.Category ?? "A");
                    HrmsDatabase.AddParameter(command, "@PenaltyType", penalty.PenaltyType ?? "Warning");
                    HrmsDatabase.AddParameter(command, "@PenaltyValue", penalty.PenaltyValue);
                    HrmsDatabase.AddParameter(command, "@ExpiryMonths", penalty.ExpiryMonths);
                    HrmsDatabase.AddParameter(command, "@AffectsContractRenewal", penalty.AffectsContractRenewal);
                    HrmsDatabase.AddParameter(command, "@RequiresApproval", penalty.RequiresApproval);
                    HrmsDatabase.AddParameter(command, "@RequiresAttachment", penalty.RequiresAttachment);
                    HrmsDatabase.AddParameter(command, "@IsActive", penalty.IsActive);
                });
        }

        TempData["SuccessMessage"] = "تم حفظ إعدادات العقوبات والجزاءات.";
        return RedirectToPage();
    }

    private async Task PrepareAsync()
    {
        await PrepareSchemaAsync();
        await SeedDefaultsAsync();
        await LoadSocialAsync();
        await LoadTaxAsync();
        await LoadLeavesAsync();
        await LoadNoticeAsync();
        await LoadPenaltiesAsync();

        SuccessMessage = TempData["SuccessMessage"]?.ToString();
    }

    private async Task PrepareSchemaAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF OBJECT_ID('HrSocialSecuritySettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrSocialSecuritySettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PolicyName nvarchar(150) NOT NULL,
        IsEnabled bit NOT NULL DEFAULT(1),
        EmployeeRate decimal(9,4) NOT NULL DEFAULT(0),
        CompanyRate decimal(9,4) NOT NULL DEFAULT(0),
        MinSalary decimal(18,2) NULL,
        MaxSalary decimal(18,2) NULL,
        ScopeType nvarchar(80) NULL,
        EffectiveFrom date NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(200) NULL
    );
END;

IF OBJECT_ID('HrTaxSettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrTaxSettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PolicyName nvarchar(150) NOT NULL,
        IsEnabled bit NOT NULL DEFAULT(1),
        ExemptionAmount decimal(18,2) NOT NULL DEFAULT(0),
        CalculationMethod nvarchar(80) NULL,
        ScopeType nvarchar(80) NULL,
        EffectiveFrom date NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(200) NULL
    );
END;

IF OBJECT_ID('HrTaxBrackets', 'U') IS NULL
BEGIN
    CREATE TABLE HrTaxBrackets
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FromAmount decimal(18,2) NOT NULL DEFAULT(0),
        ToAmount decimal(18,2) NULL,
        Rate decimal(9,4) NOT NULL DEFAULT(0),
        SortOrder int NOT NULL DEFAULT(0)
    );
END;

IF OBJECT_ID('HrLeavePolicySettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrLeavePolicySettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LeaveType nvarchar(120) NOT NULL,
        AnnualBalance decimal(9,2) NOT NULL DEFAULT(0),
        AccrualMethod nvarchar(80) NULL,
        AllowCarryForward bit NOT NULL DEFAULT(0),
        MaxCarryForward decimal(9,2) NOT NULL DEFAULT(0),
        IsPaid bit NOT NULL DEFAULT(1),
        RequiresApproval bit NOT NULL DEFAULT(1),
        EffectiveFrom date NOT NULL,
        SortOrder int NOT NULL DEFAULT(0)
    );
END;

IF OBJECT_ID('HrNoticePolicySettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrNoticePolicySettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PolicyName nvarchar(150) NOT NULL,
        NoticeDays int NOT NULL DEFAULT(30),
        AppliesTo nvarchar(150) NULL,
        EffectiveFrom date NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(200) NULL
    );
END;

IF OBJECT_ID('HrPenaltyPolicySettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrPenaltyPolicySettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code nvarchar(50) NULL,
        PenaltyName nvarchar(200) NOT NULL,
        Category nvarchar(10) NULL,
        PenaltyType nvarchar(80) NULL,
        PenaltyValue decimal(18,2) NOT NULL DEFAULT(0),
        ExpiryMonths int NOT NULL DEFAULT(3),
        AffectsContractRenewal bit NOT NULL DEFAULT(0),
        RequiresApproval bit NOT NULL DEFAULT(1),
        RequiresAttachment bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1)
    );
END;");
    }

    private async Task SeedDefaultsAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF NOT EXISTS (SELECT 1 FROM HrSocialSecuritySettings)
BEGIN
    INSERT INTO HrSocialSecuritySettings
    (PolicyName, IsEnabled, EmployeeRate, CompanyRate, MinSalary, MaxSalary, ScopeType, EffectiveFrom, UpdatedAt)
    VALUES
    ('Default Social Security Policy', 1, 5.0000, 12.0000, 0, NULL, 'Company', CAST(GETDATE() AS date), GETDATE());
END;

IF NOT EXISTS (SELECT 1 FROM HrTaxSettings)
BEGIN
    INSERT INTO HrTaxSettings
    (PolicyName, IsEnabled, ExemptionAmount, CalculationMethod, ScopeType, EffectiveFrom, UpdatedAt)
    VALUES
    ('Default Tax Policy', 0, 0, 'Brackets', 'Company', CAST(GETDATE() AS date), GETDATE());
END;

IF NOT EXISTS (SELECT 1 FROM HrTaxBrackets)
BEGIN
    INSERT INTO HrTaxBrackets (FromAmount, ToAmount, Rate, SortOrder)
    VALUES
    (0, 500000, 0, 1),
    (500001, 1000000, 5, 2),
    (1000001, NULL, 10, 3);
END;

IF NOT EXISTS (SELECT 1 FROM HrLeavePolicySettings)
BEGIN
    INSERT INTO HrLeavePolicySettings
    (LeaveType, AnnualBalance, AccrualMethod, AllowCarryForward, MaxCarryForward, IsPaid, RequiresApproval, EffectiveFrom, SortOrder)
    VALUES
    (N'سنوية', 21, 'Annual', 1, 7, 1, 1, CAST(GETDATE() AS date), 1),
    (N'مرضية', 0, 'Manual', 0, 0, 1, 1, CAST(GETDATE() AS date), 2),
    (N'بدون راتب', 0, 'Manual', 0, 0, 0, 1, CAST(GETDATE() AS date), 3);
END;

IF NOT EXISTS (SELECT 1 FROM HrNoticePolicySettings)
BEGIN
    INSERT INTO HrNoticePolicySettings
    (PolicyName, NoticeDays, AppliesTo, EffectiveFrom, UpdatedAt)
    VALUES
    ('Default Notice Period', 30, 'All Employees', CAST(GETDATE() AS date), GETDATE());
END;

IF NOT EXISTS (SELECT 1 FROM HrPenaltyPolicySettings)
BEGIN
    INSERT INTO HrPenaltyPolicySettings
    (Code, PenaltyName, Category, PenaltyType, PenaltyValue, ExpiryMonths, AffectsContractRenewal, RequiresApproval, RequiresAttachment, IsActive)
    VALUES
    ('A01', N'تنبيه شفهي / كتابي', 'A', 'Warning', 0, 3, 0, 0, 0, 1),
    ('B01', N'خصم يوم', 'B', 'DeductDay', 1, 6, 1, 1, 0, 1),
    ('C01', N'إنذار بالفصل', 'C', 'FinalWarning', 0, 12, 1, 1, 1, 1);
END;");
    }

    private async Task LoadSocialAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT TOP 1 * FROM HrSocialSecuritySettings ORDER BY Id;",
            command => { },
            reader => new SocialSecurityInput
            {
                IsEnabled = GetBool(reader, "IsEnabled"),
                EmployeeRate = GetDecimal(reader, "EmployeeRate"),
                CompanyRate = GetDecimal(reader, "CompanyRate"),
                MinSalary = GetDecimal(reader, "MinSalary"),
                MaxSalary = GetDecimal(reader, "MaxSalary"),
                ScopeType = GetString(reader, "ScopeType"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom")
            });

        Social = rows.FirstOrDefault() ?? new SocialSecurityInput();
    }

    private async Task LoadTaxAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT TOP 1 * FROM HrTaxSettings ORDER BY Id;",
            command => { },
            reader => new TaxInput
            {
                IsEnabled = GetBool(reader, "IsEnabled"),
                ExemptionAmount = GetDecimal(reader, "ExemptionAmount"),
                CalculationMethod = GetString(reader, "CalculationMethod"),
                ScopeType = GetString(reader, "ScopeType"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom")
            });

        Tax = rows.FirstOrDefault() ?? new TaxInput();

        TaxBrackets = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT * FROM HrTaxBrackets ORDER BY SortOrder, Id;",
            command => { },
            reader => new TaxBracketInput
            {
                FromAmount = GetDecimal(reader, "FromAmount"),
                ToAmount = GetDecimal(reader, "ToAmount"),
                Rate = GetDecimal(reader, "Rate")
            });

        EnsureBracketRows();
    }

    private async Task LoadLeavesAsync()
    {
        LeavePolicies = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT * FROM HrLeavePolicySettings ORDER BY SortOrder, Id;",
            command => { },
            reader => new LeavePolicyInput
            {
                LeaveType = GetString(reader, "LeaveType"),
                AnnualBalance = GetDecimal(reader, "AnnualBalance"),
                AccrualMethod = GetString(reader, "AccrualMethod"),
                AllowCarryForward = GetBool(reader, "AllowCarryForward"),
                MaxCarryForward = GetDecimal(reader, "MaxCarryForward"),
                IsPaid = GetBool(reader, "IsPaid"),
                RequiresApproval = GetBool(reader, "RequiresApproval"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom")
            });

        while (LeavePolicies.Count < 5)
        {
            LeavePolicies.Add(new LeavePolicyInput { EffectiveFrom = DateOnly.FromDateTime(DateTime.Today) });
        }
    }

    private async Task LoadNoticeAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT TOP 1 * FROM HrNoticePolicySettings ORDER BY Id;",
            command => { },
            reader => new NoticePolicyInput
            {
                PolicyName = GetString(reader, "PolicyName"),
                NoticeDays = GetInt(reader, "NoticeDays"),
                AppliesTo = GetString(reader, "AppliesTo"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom")
            });

        Notice = rows.FirstOrDefault() ?? new NoticePolicyInput();
    }

    private async Task LoadPenaltiesAsync()
    {
        Penalties = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT * FROM HrPenaltyPolicySettings ORDER BY Category, Id;",
            command => { },
            reader => new PenaltyPolicyInput
            {
                Code = GetString(reader, "Code"),
                PenaltyName = GetString(reader, "PenaltyName"),
                Category = GetString(reader, "Category"),
                PenaltyType = GetString(reader, "PenaltyType"),
                PenaltyValue = GetDecimal(reader, "PenaltyValue"),
                ExpiryMonths = GetInt(reader, "ExpiryMonths"),
                AffectsContractRenewal = GetBool(reader, "AffectsContractRenewal"),
                RequiresApproval = GetBool(reader, "RequiresApproval"),
                RequiresAttachment = GetBool(reader, "RequiresAttachment"),
                IsActive = GetBool(reader, "IsActive")
            });

        while (Penalties.Count < 6)
        {
            Penalties.Add(new PenaltyPolicyInput { IsActive = true, RequiresApproval = true, ExpiryMonths = 3, Category = "A" });
        }
    }

    private TaxBracketInput GetBracket(int index)
    {
        EnsureBracketRows();
        return TaxBrackets[index];
    }

    private void EnsureBracketRows()
    {
        while (TaxBrackets.Count < 3)
        {
            TaxBrackets.Add(new TaxBracketInput());
        }
    }

    private string CurrentUser()
    {
        return Request.Cookies["SA.UserName"] ?? "System";
    }

    private static object ToSqlDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
    }

    private static string GetString(System.Data.Common.DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static int GetInt(System.Data.Common.DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static bool GetBool(System.Data.Common.DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal));
    }

    private static decimal GetDecimal(System.Data.Common.DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    public class SocialSecurityInput
    {
        public bool IsEnabled { get; set; } = true;
        public decimal EmployeeRate { get; set; } = 5;
        public decimal CompanyRate { get; set; } = 12;
        public decimal MinSalary { get; set; }
        public decimal MaxSalary { get; set; }
        public string ScopeType { get; set; } = "Company";
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }

    public class TaxInput
    {
        public bool IsEnabled { get; set; }
        public decimal ExemptionAmount { get; set; }
        public string CalculationMethod { get; set; } = "Brackets";
        public string ScopeType { get; set; } = "Company";
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }

    public class TaxBracketInput
    {
        public decimal FromAmount { get; set; }
        public decimal ToAmount { get; set; }
        public decimal Rate { get; set; }
    }

    public class LeavePolicyInput
    {
        public string LeaveType { get; set; } = string.Empty;
        public decimal AnnualBalance { get; set; }
        public string AccrualMethod { get; set; } = "Annual";
        public bool AllowCarryForward { get; set; }
        public decimal MaxCarryForward { get; set; }
        public bool IsPaid { get; set; } = true;
        public bool RequiresApproval { get; set; } = true;
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }

    public class NoticePolicyInput
    {
        public string PolicyName { get; set; } = "Default Notice Period";
        public int NoticeDays { get; set; } = 30;
        public string AppliesTo { get; set; } = "All Employees";
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }

    public class PenaltyPolicyInput
    {
        public string Code { get; set; } = string.Empty;
        public string PenaltyName { get; set; } = string.Empty;
        public string Category { get; set; } = "A";
        public string PenaltyType { get; set; } = "Warning";
        public decimal PenaltyValue { get; set; }
        public int ExpiryMonths { get; set; } = 3;
        public bool AffectsContractRenewal { get; set; }
        public bool RequiresApproval { get; set; } = true;
        public bool RequiresAttachment { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
