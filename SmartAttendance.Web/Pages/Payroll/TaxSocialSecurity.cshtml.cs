using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

public class TaxSocialSecurityModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public TaxSocialSecurityModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? PayrollMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 100;

    [BindProperty]
    public TaxSocialSecurityInput Input { get; set; } = new();

    [BindProperty]
    public TaxSocialSecuritySettingsInput SettingsInput { get; set; } = new();

    public TaxSocialSecuritySettingsInput Settings { get; set; } = new();

    public List<TaxSocialSecurityRow> Rows { get; set; } = new();

    public List<EmployeeOption> EmployeeOptions { get; set; } = new();

    public int TotalEmployees { get; set; }
    public int RecordedEmployees { get; set; }
    public int MissingRecords { get; set; }
    public int RowsWithGrossSalary { get; set; }
    public decimal TotalGrossSalary { get; set; }
    public decimal TotalTaxableSalary { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TotalSocialEmployee { get; set; }
    public decimal TotalSocialCompany { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNetSalary { get; set; }

    public string NormalizedMonth => NormalizeMonth(PayrollMonth);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync()
    {
        await EnsurePageTablesAsync();

        var month = NormalizeMonth(SettingsInput.PayrollMonth);
        var userName = CurrentUserName();

        SettingsInput.TaxRate = NormalizePercent(SettingsInput.TaxRate);
        SettingsInput.EmployeeSocialSecurityRate = NormalizePercent(SettingsInput.EmployeeSocialSecurityRate);
        SettingsInput.CompanySocialSecurityRate = NormalizePercent(SettingsInput.CompanySocialSecurityRate);
        SettingsInput.TaxExemptionAmount = NormalizeMoney(SettingsInput.TaxExemptionAmount);
        SettingsInput.SocialSecurityCeiling = NormalizeMoney(SettingsInput.SocialSecurityCeiling);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF EXISTS (SELECT 1 FROM PayrollTaxSocialSecuritySettings WHERE PayrollMonth = @PayrollMonth)
BEGIN
    UPDATE PayrollTaxSocialSecuritySettings
    SET
        TaxRate = @TaxRate,
        TaxExemptionAmount = @TaxExemptionAmount,
        EmployeeSocialSecurityRate = @EmployeeSocialSecurityRate,
        CompanySocialSecurityRate = @CompanySocialSecurityRate,
        SocialSecurityCeiling = @SocialSecurityCeiling,
        RoundAmounts = @RoundAmounts,
        Notes = @Notes,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @UpdatedBy
    WHERE PayrollMonth = @PayrollMonth;
END
ELSE
BEGIN
    INSERT INTO PayrollTaxSocialSecuritySettings
    (
        PayrollMonth,
        TaxRate,
        TaxExemptionAmount,
        EmployeeSocialSecurityRate,
        CompanySocialSecurityRate,
        SocialSecurityCeiling,
        RoundAmounts,
        Notes,
        CreatedAt,
        CreatedBy
    )
    VALUES
    (
        @PayrollMonth,
        @TaxRate,
        @TaxExemptionAmount,
        @EmployeeSocialSecurityRate,
        @CompanySocialSecurityRate,
        @SocialSecurityCeiling,
        @RoundAmounts,
        @Notes,
        SYSUTCDATETIME(),
        @UpdatedBy
    );
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PayrollMonth", month);
                HrmsDatabase.AddParameter(command, "@TaxRate", SettingsInput.TaxRate);
                HrmsDatabase.AddParameter(command, "@TaxExemptionAmount", SettingsInput.TaxExemptionAmount);
                HrmsDatabase.AddParameter(command, "@EmployeeSocialSecurityRate", SettingsInput.EmployeeSocialSecurityRate);
                HrmsDatabase.AddParameter(command, "@CompanySocialSecurityRate", SettingsInput.CompanySocialSecurityRate);
                HrmsDatabase.AddParameter(command, "@SocialSecurityCeiling", SettingsInput.SocialSecurityCeiling);
                HrmsDatabase.AddParameter(command, "@RoundAmounts", SettingsInput.RoundAmounts);
                HrmsDatabase.AddParameter(command, "@Notes", SettingsInput.Notes);
                HrmsDatabase.AddParameter(command, "@UpdatedBy", userName);
            });

        await WriteAuditAsync(
            "PayrollTaxSocialSecuritySettings",
            month,
            "SaveSettings",
            "Tax/Social settings saved for month " + month,
            userName);

        TempData["TaxSocialSuccess"] = "تم حفظ إعدادات الضريبة والضمان للشهر المحدد.";
        return RedirectToPage(new { PayrollMonth = month, SearchTerm, MaxRows });
    }

    public async Task<IActionResult> OnPostApplySettingsAsync()
    {
        await EnsurePageTablesAsync();

        var month = NormalizeMonth(SettingsInput.PayrollMonth);
        var settings = await LoadSettingsAsync(month);
        var userName = CurrentUserName();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE PayrollTaxSocialSecurityRecords
SET
    TaxableSalary =
        CASE
            WHEN @RoundAmounts = 1 THEN ROUND(CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END, 0)
            ELSE ROUND(CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END, 2)
        END,

    TaxAmount =
        CASE
            WHEN @RoundAmounts = 1 THEN ROUND((CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END) * @TaxRate / 100, 0)
            ELSE ROUND((CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END) * @TaxRate / 100, 2)
        END,

    SocialSecurityEmployeeAmount =
        CASE
            WHEN @RoundAmounts = 1 THEN
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @EmployeeSocialSecurityRate / 100, 0)
            ELSE
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @EmployeeSocialSecurityRate / 100, 2)
        END,

    SocialSecurityCompanyAmount =
        CASE
            WHEN @RoundAmounts = 1 THEN
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @CompanySocialSecurityRate / 100, 0)
            ELSE
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @CompanySocialSecurityRate / 100, 2)
        END,

    NetSalary =
        GrossSalary
        -
        CASE
            WHEN @RoundAmounts = 1 THEN ROUND((CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END) * @TaxRate / 100, 0)
            ELSE ROUND((CASE WHEN GrossSalary - @TaxExemptionAmount > 0 THEN GrossSalary - @TaxExemptionAmount ELSE 0 END) * @TaxRate / 100, 2)
        END
        -
        CASE
            WHEN @RoundAmounts = 1 THEN
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @EmployeeSocialSecurityRate / 100, 0)
            ELSE
                ROUND((CASE WHEN @SocialSecurityCeiling > 0 AND GrossSalary > @SocialSecurityCeiling THEN @SocialSecurityCeiling ELSE GrossSalary END) * @EmployeeSocialSecurityRate / 100, 2)
        END,

    Notes =
        CASE
            WHEN Notes IS NULL OR LTRIM(RTRIM(Notes)) = '' THEN N'تم الاحتساب حسب إعدادات الشهر'
            ELSE Notes
        END,

    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @UpdatedBy
WHERE PayrollMonth = @PayrollMonth
  AND GrossSalary > 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PayrollMonth", month);
                HrmsDatabase.AddParameter(command, "@TaxRate", settings.TaxRate);
                HrmsDatabase.AddParameter(command, "@TaxExemptionAmount", settings.TaxExemptionAmount);
                HrmsDatabase.AddParameter(command, "@EmployeeSocialSecurityRate", settings.EmployeeSocialSecurityRate);
                HrmsDatabase.AddParameter(command, "@CompanySocialSecurityRate", settings.CompanySocialSecurityRate);
                HrmsDatabase.AddParameter(command, "@SocialSecurityCeiling", settings.SocialSecurityCeiling);
                HrmsDatabase.AddParameter(command, "@RoundAmounts", settings.RoundAmounts);
                HrmsDatabase.AddParameter(command, "@UpdatedBy", userName);
            });

        await WriteAuditAsync(
            "PayrollTaxSocialSecurity",
            month,
            "ApplySettings",
            "Tax/Social settings applied to saved records with gross salary for month " + month,
            userName);

        TempData["TaxSocialSuccess"] = "تم تطبيق إعدادات الشهر على السجلات المحفوظة التي تحتوي راتب إجمالي.";
        return RedirectToPage(new { PayrollMonth = month, SearchTerm, MaxRows });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await EnsurePageTablesAsync();

        var month = NormalizeMonth(Input.PayrollMonth);
        var userName = CurrentUserName();

        if (Input.EmployeeId <= 0)
        {
            TempData["TaxSocialError"] = "يرجى اختيار الموظف.";
            return RedirectToPage(new { PayrollMonth = month, SearchTerm, MaxRows });
        }

        Input.GrossSalary = NormalizeMoney(Input.GrossSalary);
        Input.TaxableSalary = NormalizeMoney(Input.TaxableSalary);
        Input.TaxAmount = NormalizeMoney(Input.TaxAmount);
        Input.SocialSecurityEmployeeAmount = NormalizeMoney(Input.SocialSecurityEmployeeAmount);
        Input.SocialSecurityCompanyAmount = NormalizeMoney(Input.SocialSecurityCompanyAmount);

        if (Input.TaxableSalary == 0 && Input.GrossSalary > 0)
        {
            Input.TaxableSalary = Input.GrossSalary;
        }

        Input.NetSalary = Input.GrossSalary - Input.TaxAmount - Input.SocialSecurityEmployeeAmount;

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF EXISTS
(
    SELECT 1
    FROM PayrollTaxSocialSecurityRecords
    WHERE EmployeeId = @EmployeeId
      AND PayrollMonth = @PayrollMonth
)
BEGIN
    UPDATE PayrollTaxSocialSecurityRecords
    SET
        GrossSalary = @GrossSalary,
        TaxableSalary = @TaxableSalary,
        TaxAmount = @TaxAmount,
        SocialSecurityEmployeeAmount = @SocialSecurityEmployeeAmount,
        SocialSecurityCompanyAmount = @SocialSecurityCompanyAmount,
        NetSalary = @NetSalary,
        Notes = @Notes,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @UpdatedBy
    WHERE EmployeeId = @EmployeeId
      AND PayrollMonth = @PayrollMonth;
END
ELSE
BEGIN
    INSERT INTO PayrollTaxSocialSecurityRecords
    (
        EmployeeId,
        PayrollMonth,
        GrossSalary,
        TaxableSalary,
        TaxAmount,
        SocialSecurityEmployeeAmount,
        SocialSecurityCompanyAmount,
        NetSalary,
        Notes,
        CreatedAt,
        CreatedBy
    )
    VALUES
    (
        @EmployeeId,
        @PayrollMonth,
        @GrossSalary,
        @TaxableSalary,
        @TaxAmount,
        @SocialSecurityEmployeeAmount,
        @SocialSecurityCompanyAmount,
        @NetSalary,
        @Notes,
        SYSUTCDATETIME(),
        @UpdatedBy
    );
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId);
                HrmsDatabase.AddParameter(command, "@PayrollMonth", month);
                HrmsDatabase.AddParameter(command, "@GrossSalary", Input.GrossSalary);
                HrmsDatabase.AddParameter(command, "@TaxableSalary", Input.TaxableSalary);
                HrmsDatabase.AddParameter(command, "@TaxAmount", Input.TaxAmount);
                HrmsDatabase.AddParameter(command, "@SocialSecurityEmployeeAmount", Input.SocialSecurityEmployeeAmount);
                HrmsDatabase.AddParameter(command, "@SocialSecurityCompanyAmount", Input.SocialSecurityCompanyAmount);
                HrmsDatabase.AddParameter(command, "@NetSalary", Input.NetSalary);
                HrmsDatabase.AddParameter(command, "@Notes", Input.Notes);
                HrmsDatabase.AddParameter(command, "@UpdatedBy", userName);
            });

        await WriteAuditAsync(
            "PayrollTaxSocialSecurity",
            Input.EmployeeId + "|" + month,
            "SaveRecord",
            "Tax/Social record saved",
            userName);

        TempData["TaxSocialSuccess"] = "تم حفظ بيانات الضريبة والضمان بنجاح.";
        return RedirectToPage(new { PayrollMonth = month, SearchTerm, MaxRows });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await EnsurePageTablesAsync();

        var month = NormalizeMonth(PayrollMonth);
        var userName = CurrentUserName();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DELETE FROM PayrollTaxSocialSecurityRecords
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
            });

        await WriteAuditAsync(
            "PayrollTaxSocialSecurity",
            id.ToString(),
            "DeleteRecord",
            "Tax/Social record deleted",
            userName);

        TempData["TaxSocialSuccess"] = "تم حذف السجل.";
        return RedirectToPage(new { PayrollMonth = month, SearchTerm, MaxRows });
    }

    private async Task LoadAsync()
    {
        await EnsurePageTablesAsync();

        PayrollMonth = NormalizeMonth(PayrollMonth);
        MaxRows = NormalizeMaxRows(MaxRows);

        Settings = await LoadSettingsAsync(PayrollMonth);
        SettingsInput = Settings;

        Input.PayrollMonth = PayrollMonth;

        EmployeeOptions = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1000
    Id,
    EmployeeNo,
    FullName
FROM Employees
WHERE IsActive = 1
ORDER BY EmployeeNo, FullName;
""",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName")
            });

        Rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP (@MaxRows)
    e.Id AS EmployeeId,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    p.Id AS RecordId,
    ISNULL(p.PayrollMonth, @PayrollMonth) AS PayrollMonth,
    ISNULL(p.GrossSalary, 0) AS GrossSalary,
    ISNULL(p.TaxableSalary, 0) AS TaxableSalary,
    ISNULL(p.TaxAmount, 0) AS TaxAmount,
    ISNULL(p.SocialSecurityEmployeeAmount, 0) AS SocialSecurityEmployeeAmount,
    ISNULL(p.SocialSecurityCompanyAmount, 0) AS SocialSecurityCompanyAmount,
    ISNULL(p.NetSalary, 0) AS NetSalary,
    ISNULL(p.Notes, '') AS Notes,
    p.UpdatedAt,
    p.CreatedAt
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN PayrollTaxSocialSecurityRecords p
       ON p.EmployeeId = e.Id
      AND p.PayrollMonth = @PayrollMonth
WHERE e.IsActive = 1
  AND
  (
      @SearchTerm = ''
      OR e.EmployeeNo LIKE '%' + @SearchTerm + '%'
      OR e.FullName LIKE '%' + @SearchTerm + '%'
      OR ISNULL(e.Position, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(d.Name, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(b.Name, '') LIKE '%' + @SearchTerm + '%'
  )
ORDER BY e.EmployeeNo, e.FullName;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PayrollMonth", PayrollMonth);
                HrmsDatabase.AddParameter(command, "@SearchTerm", Clean(SearchTerm));
                HrmsDatabase.AddParameter(command, "@MaxRows", MaxRows);
            },
            reader => new TaxSocialSecurityRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                RecordId = HrmsDatabase.GetInt(reader, "RecordId"),
                PayrollMonth = HrmsDatabase.GetString(reader, "PayrollMonth"),
                GrossSalary = GetDecimal(reader, "GrossSalary"),
                TaxableSalary = GetDecimal(reader, "TaxableSalary"),
                TaxAmount = GetDecimal(reader, "TaxAmount"),
                SocialSecurityEmployeeAmount = GetDecimal(reader, "SocialSecurityEmployeeAmount"),
                SocialSecurityCompanyAmount = GetDecimal(reader, "SocialSecurityCompanyAmount"),
                NetSalary = GetDecimal(reader, "NetSalary"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UpdatedAt = HrmsDatabase.GetDateTime(reader, "UpdatedAt"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        TotalEmployees = Rows.Count;
        RecordedEmployees = Rows.Count(x => x.RecordId > 0);
        MissingRecords = TotalEmployees - RecordedEmployees;
        RowsWithGrossSalary = Rows.Count(x => x.GrossSalary > 0);
        TotalGrossSalary = Rows.Sum(x => x.GrossSalary);
        TotalTaxableSalary = Rows.Sum(x => x.TaxableSalary);
        TotalTaxAmount = Rows.Sum(x => x.TaxAmount);
        TotalSocialEmployee = Rows.Sum(x => x.SocialSecurityEmployeeAmount);
        TotalSocialCompany = Rows.Sum(x => x.SocialSecurityCompanyAmount);
        TotalDeductions = TotalTaxAmount + TotalSocialEmployee;
        TotalNetSalary = Rows.Sum(x => x.NetSalary);
    }

    private async Task<TaxSocialSecuritySettingsInput> LoadSettingsAsync(string month)
    {
        var result = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    PayrollMonth,
    TaxRate,
    TaxExemptionAmount,
    EmployeeSocialSecurityRate,
    CompanySocialSecurityRate,
    SocialSecurityCeiling,
    RoundAmounts,
    ISNULL(Notes, '') AS Notes
FROM PayrollTaxSocialSecuritySettings
WHERE PayrollMonth = @PayrollMonth;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PayrollMonth", month);
            },
            reader => new TaxSocialSecuritySettingsInput
            {
                PayrollMonth = HrmsDatabase.GetString(reader, "PayrollMonth"),
                TaxRate = GetDecimal(reader, "TaxRate"),
                TaxExemptionAmount = GetDecimal(reader, "TaxExemptionAmount"),
                EmployeeSocialSecurityRate = GetDecimal(reader, "EmployeeSocialSecurityRate"),
                CompanySocialSecurityRate = GetDecimal(reader, "CompanySocialSecurityRate"),
                SocialSecurityCeiling = GetDecimal(reader, "SocialSecurityCeiling"),
                RoundAmounts = GetBool(reader, "RoundAmounts"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });

        return result.FirstOrDefault() ?? new TaxSocialSecuritySettingsInput
        {
            PayrollMonth = month,
            TaxRate = 0,
            TaxExemptionAmount = 0,
            EmployeeSocialSecurityRate = 0,
            CompanySocialSecurityRate = 0,
            SocialSecurityCeiling = 0,
            RoundAmounts = true,
            Notes = "ضع النسب حسب السياسة المعتمدة قبل تطبيق الاحتساب."
        };
    }

    private async Task EnsurePageTablesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('PayrollTaxSocialSecurityRecords', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollTaxSocialSecurityRecords
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        PayrollMonth nvarchar(7) NOT NULL,
        GrossSalary decimal(18,2) NOT NULL DEFAULT(0),
        TaxableSalary decimal(18,2) NOT NULL DEFAULT(0),
        TaxAmount decimal(18,2) NOT NULL DEFAULT(0),
        SocialSecurityEmployeeAmount decimal(18,2) NOT NULL DEFAULT(0),
        SocialSecurityCompanyAmount decimal(18,2) NOT NULL DEFAULT(0),
        NetSalary decimal(18,2) NOT NULL DEFAULT(0),
        Notes nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL
    );
END;

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'TaxableSalary') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD TaxableSalary decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'TaxAmount') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD TaxAmount decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'SocialSecurityEmployeeAmount') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD SocialSecurityEmployeeAmount decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'SocialSecurityCompanyAmount') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD SocialSecurityCompanyAmount decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'NetSalary') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD NetSalary decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecurityRecords', 'Notes') IS NULL
    ALTER TABLE PayrollTaxSocialSecurityRecords ADD Notes nvarchar(max) NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PayrollTaxSocialSecurityRecords_Employee_Month'
      AND object_id = OBJECT_ID('PayrollTaxSocialSecurityRecords')
)
BEGIN
    CREATE UNIQUE INDEX IX_PayrollTaxSocialSecurityRecords_Employee_Month
    ON PayrollTaxSocialSecurityRecords(EmployeeId, PayrollMonth);
END;

IF OBJECT_ID('PayrollTaxSocialSecuritySettings', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollTaxSocialSecuritySettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PayrollMonth nvarchar(7) NOT NULL,
        TaxRate decimal(9,4) NOT NULL DEFAULT(0),
        TaxExemptionAmount decimal(18,2) NOT NULL DEFAULT(0),
        EmployeeSocialSecurityRate decimal(9,4) NOT NULL DEFAULT(0),
        CompanySocialSecurityRate decimal(9,4) NOT NULL DEFAULT(0),
        SocialSecurityCeiling decimal(18,2) NOT NULL DEFAULT(0),
        RoundAmounts bit NOT NULL DEFAULT(1),
        Notes nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL
    );
END;

IF COL_LENGTH('PayrollTaxSocialSecuritySettings', 'TaxExemptionAmount') IS NULL
    ALTER TABLE PayrollTaxSocialSecuritySettings ADD TaxExemptionAmount decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecuritySettings', 'SocialSecurityCeiling') IS NULL
    ALTER TABLE PayrollTaxSocialSecuritySettings ADD SocialSecurityCeiling decimal(18,2) NOT NULL DEFAULT(0);

IF COL_LENGTH('PayrollTaxSocialSecuritySettings', 'RoundAmounts') IS NULL
    ALTER TABLE PayrollTaxSocialSecuritySettings ADD RoundAmounts bit NOT NULL DEFAULT(1);

IF COL_LENGTH('PayrollTaxSocialSecuritySettings', 'Notes') IS NULL
    ALTER TABLE PayrollTaxSocialSecuritySettings ADD Notes nvarchar(max) NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PayrollTaxSocialSecuritySettings_Month'
      AND object_id = OBJECT_ID('PayrollTaxSocialSecuritySettings')
)
BEGIN
    CREATE UNIQUE INDEX IX_PayrollTaxSocialSecuritySettings_Month
    ON PayrollTaxSocialSecuritySettings(PayrollMonth);
END;
""");
    }

    private async Task WriteAuditAsync(string entityName, string entityId, string action, string details, string userName)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'EntityName') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'EntityId') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'Action') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'NewValues') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'UserName') IS NOT NULL
AND COL_LENGTH('AuditLogs', 'CreatedAt') IS NOT NULL
BEGIN
    DECLARE @sql nvarchar(max) =
    N'INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress, CreatedAt)
      VALUES (@EntityName, @EntityId, @Action, NULL, @NewValues, @UserName, NULL, SYSUTCDATETIME());';

    EXEC sp_executesql
        @sql,
        N'@EntityName nvarchar(150), @EntityId nvarchar(150), @Action nvarchar(100), @NewValues nvarchar(max), @UserName nvarchar(150)',
        @EntityName = @EntityName,
        @EntityId = @EntityId,
        @Action = @Action,
        @NewValues = @NewValues,
        @UserName = @UserName;
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EntityName", entityName);
                HrmsDatabase.AddParameter(command, "@EntityId", entityId);
                HrmsDatabase.AddParameter(command, "@Action", action);
                HrmsDatabase.AddParameter(command, "@NewValues", details);
                HrmsDatabase.AddParameter(command, "@UserName", userName);
            });
    }

    private string CurrentUserName()
    {
        return User?.Identity?.Name ?? "system";
    }

    private static string NormalizeMonth(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Length == 7 &&
            value[4] == '-' &&
            int.TryParse(value[..4], out _) &&
            int.TryParse(value[5..], out var monthNumber) &&
            monthNumber >= 1 &&
            monthNumber <= 12)
        {
            return value;
        }

        return DateTime.Today.ToString("yyyy-MM");
    }

    private static int NormalizeMaxRows(int value)
    {
        if (value <= 0)
        {
            return 100;
        }

        return value > 1000 ? 1000 : value;
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static decimal NormalizeMoney(decimal value)
    {
        return value < 0 ? 0 : value;
    }

    private static decimal NormalizePercent(decimal value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 100 ? 100 : value;
    }

    private static decimal GetDecimal(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static bool GetBool(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        return Convert.ToBoolean(reader.GetValue(ordinal));
    }

    public class TaxSocialSecurityInput
    {
        public int EmployeeId { get; set; }

        public string? PayrollMonth { get; set; }

        public decimal GrossSalary { get; set; }

        public decimal TaxableSalary { get; set; }

        public decimal TaxAmount { get; set; }

        public decimal SocialSecurityEmployeeAmount { get; set; }

        public decimal SocialSecurityCompanyAmount { get; set; }

        public decimal NetSalary { get; set; }

        public string? Notes { get; set; }
    }

    public class TaxSocialSecuritySettingsInput
    {
        public string? PayrollMonth { get; set; }

        public decimal TaxRate { get; set; }

        public decimal TaxExemptionAmount { get; set; }

        public decimal EmployeeSocialSecurityRate { get; set; }

        public decimal CompanySocialSecurityRate { get; set; }

        public decimal SocialSecurityCeiling { get; set; }

        public bool RoundAmounts { get; set; } = true;

        public string? Notes { get; set; }
    }

    public class TaxSocialSecurityRow
    {
        public int RecordId { get; set; }

        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string PayrollMonth { get; set; } = string.Empty;

        public decimal GrossSalary { get; set; }

        public decimal TaxableSalary { get; set; }

        public decimal TaxAmount { get; set; }

        public decimal SocialSecurityEmployeeAmount { get; set; }

        public decimal SocialSecurityCompanyAmount { get; set; }

        public decimal NetSalary { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public bool HasRecord => RecordId > 0;

        public bool HasGrossSalary => GrossSalary > 0;
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(EmployeeNo)
            ? FullName
            : EmployeeNo + " - " + FullName;
    }
}
