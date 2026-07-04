using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Settings;

public class IraqTaxModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IraqTaxModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public TaxSettingInput Setting { get; set; } = new();

    [BindProperty]
    public List<TaxBracketInput> Brackets { get; set; } = new();

    [BindProperty]
    public TaxSampleInput Sample { get; set; } = new();

    public TaxSampleResult? Result { get; set; }

    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        await PrepareAsync();
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync()
    {
        await PrepareSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF EXISTS (SELECT 1 FROM HrIraqTaxSettings WHERE Id = 1)
BEGIN
    UPDATE HrIraqTaxSettings
    SET IsEnabled = @IsEnabled,
        TaxBasis = @TaxBasis,
        SelfAllowance = @SelfAllowance,
        WifeHousewifeAllowance = @WifeHousewifeAllowance,
        ChildAllowance = @ChildAllowance,
        WidowOrDivorcedAllowance = @WidowOrDivorcedAllowance,
        AgeAllowance = @AgeAllowance,
        AgeAllowanceFrom = @AgeAllowanceFrom,
        PrivateSectorAllowanceExemptPercent = @PrivateSectorAllowanceExemptPercent,
        EffectiveFrom = @EffectiveFrom,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = 1;
END
ELSE
BEGIN
    SET IDENTITY_INSERT HrIraqTaxSettings ON;
    INSERT INTO HrIraqTaxSettings
    (
        Id,
        PolicyName,
        IsEnabled,
        TaxBasis,
        SelfAllowance,
        WifeHousewifeAllowance,
        ChildAllowance,
        WidowOrDivorcedAllowance,
        AgeAllowance,
        AgeAllowanceFrom,
        PrivateSectorAllowanceExemptPercent,
        EffectiveFrom,
        UpdatedAt,
        UpdatedBy
    )
    VALUES
    (
        1,
        'Iraqi Income Tax Policy',
        @IsEnabled,
        @TaxBasis,
        @SelfAllowance,
        @WifeHousewifeAllowance,
        @ChildAllowance,
        @WidowOrDivorcedAllowance,
        @AgeAllowance,
        @AgeAllowanceFrom,
        @PrivateSectorAllowanceExemptPercent,
        @EffectiveFrom,
        GETDATE(),
        @UpdatedBy
    );
    SET IDENTITY_INSERT HrIraqTaxSettings OFF;
END;

DELETE FROM HrIraqTaxBrackets;

INSERT INTO HrIraqTaxBrackets (FromAmount, ToAmount, Rate, SortOrder)
VALUES
(@From1, @To1, @Rate1, 1),
(@From2, @To2, @Rate2, 2),
(@From3, @To3, @Rate3, 3),
(@From4, @To4, @Rate4, 4);",
            command =>
            {
                EnsureBracketRows();

                HrmsDatabase.AddParameter(command, "@IsEnabled", Setting.IsEnabled);
                HrmsDatabase.AddParameter(command, "@TaxBasis", Setting.TaxBasis ?? "Annual");
                HrmsDatabase.AddParameter(command, "@SelfAllowance", Setting.SelfAllowance);
                HrmsDatabase.AddParameter(command, "@WifeHousewifeAllowance", Setting.WifeHousewifeAllowance);
                HrmsDatabase.AddParameter(command, "@ChildAllowance", Setting.ChildAllowance);
                HrmsDatabase.AddParameter(command, "@WidowOrDivorcedAllowance", Setting.WidowOrDivorcedAllowance);
                HrmsDatabase.AddParameter(command, "@AgeAllowance", Setting.AgeAllowance);
                HrmsDatabase.AddParameter(command, "@AgeAllowanceFrom", Setting.AgeAllowanceFrom);
                HrmsDatabase.AddParameter(command, "@PrivateSectorAllowanceExemptPercent", Setting.PrivateSectorAllowanceExemptPercent);
                HrmsDatabase.AddParameter(command, "@EffectiveFrom", ToSqlDate(Setting.EffectiveFrom));
                HrmsDatabase.AddParameter(command, "@UpdatedBy", CurrentUser());

                for (var i = 0; i < 4; i++)
                {
                    HrmsDatabase.AddParameter(command, "@From" + (i + 1), Brackets[i].FromAmount);
                    HrmsDatabase.AddParameter(command, "@To" + (i + 1), Brackets[i].ToAmount <= 0 ? DBNull.Value : Brackets[i].ToAmount);
                    HrmsDatabase.AddParameter(command, "@Rate" + (i + 1), Brackets[i].Rate);
                }
            });

        TempData["SuccessMessage"] = "تم حفظ إعدادات ضريبة الدخل العراقية.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCalculateAsync()
    {
        await PrepareAsync();
        Result = CalculateTax(Sample);
        return Page();
    }

    private async Task PrepareAsync()
    {
        await PrepareSchemaAsync();
        await SeedDefaultsAsync();
        await LoadSettingsAsync();
        await LoadBracketsAsync();
        SuccessMessage = TempData["SuccessMessage"]?.ToString();
    }

    private async Task PrepareSchemaAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF OBJECT_ID('HrIraqTaxSettings', 'U') IS NULL
BEGIN
    CREATE TABLE HrIraqTaxSettings
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PolicyName nvarchar(150) NOT NULL,
        IsEnabled bit NOT NULL DEFAULT(1),
        TaxBasis nvarchar(50) NOT NULL DEFAULT('Annual'),
        SelfAllowance decimal(18,2) NOT NULL DEFAULT(0),
        WifeHousewifeAllowance decimal(18,2) NOT NULL DEFAULT(0),
        ChildAllowance decimal(18,2) NOT NULL DEFAULT(0),
        WidowOrDivorcedAllowance decimal(18,2) NOT NULL DEFAULT(0),
        AgeAllowance decimal(18,2) NOT NULL DEFAULT(0),
        AgeAllowanceFrom int NOT NULL DEFAULT(63),
        PrivateSectorAllowanceExemptPercent decimal(9,4) NOT NULL DEFAULT(30),
        EffectiveFrom date NOT NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(200) NULL
    );
END;

IF OBJECT_ID('HrIraqTaxBrackets', 'U') IS NULL
BEGIN
    CREATE TABLE HrIraqTaxBrackets
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FromAmount decimal(18,2) NOT NULL DEFAULT(0),
        ToAmount decimal(18,2) NULL,
        Rate decimal(9,4) NOT NULL DEFAULT(0),
        SortOrder int NOT NULL DEFAULT(0)
    );
END;");
    }

    private async Task SeedDefaultsAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
IF NOT EXISTS (SELECT 1 FROM HrIraqTaxSettings)
BEGIN
    INSERT INTO HrIraqTaxSettings
    (
        PolicyName,
        IsEnabled,
        TaxBasis,
        SelfAllowance,
        WifeHousewifeAllowance,
        ChildAllowance,
        WidowOrDivorcedAllowance,
        AgeAllowance,
        AgeAllowanceFrom,
        PrivateSectorAllowanceExemptPercent,
        EffectiveFrom,
        UpdatedAt
    )
    VALUES
    (
        'Iraqi Income Tax Policy',
        1,
        'Annual',
        5000000,
        4000000,
        400000,
        6400000,
        600000,
        63,
        30,
        CAST(GETDATE() AS date),
        GETDATE()
    );
END;

IF NOT EXISTS (SELECT 1 FROM HrIraqTaxBrackets)
BEGIN
    INSERT INTO HrIraqTaxBrackets (FromAmount, ToAmount, Rate, SortOrder)
    VALUES
    (1, 500000, 3, 1),
    (500001, 1000000, 5, 2),
    (1000001, 2000000, 10, 3),
    (2000001, NULL, 15, 4);
END;");
    }

    private async Task LoadSettingsAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT TOP 1 * FROM HrIraqTaxSettings ORDER BY Id;",
            command => { },
            reader => new TaxSettingInput
            {
                IsEnabled = GetBool(reader, "IsEnabled"),
                TaxBasis = GetString(reader, "TaxBasis"),
                SelfAllowance = GetDecimal(reader, "SelfAllowance"),
                WifeHousewifeAllowance = GetDecimal(reader, "WifeHousewifeAllowance"),
                ChildAllowance = GetDecimal(reader, "ChildAllowance"),
                WidowOrDivorcedAllowance = GetDecimal(reader, "WidowOrDivorcedAllowance"),
                AgeAllowance = GetDecimal(reader, "AgeAllowance"),
                AgeAllowanceFrom = GetInt(reader, "AgeAllowanceFrom"),
                PrivateSectorAllowanceExemptPercent = GetDecimal(reader, "PrivateSectorAllowanceExemptPercent"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom")
            });

        Setting = rows.FirstOrDefault() ?? new TaxSettingInput();
    }

    private async Task LoadBracketsAsync()
    {
        Brackets = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"SELECT * FROM HrIraqTaxBrackets ORDER BY SortOrder, Id;",
            command => { },
            reader => new TaxBracketInput
            {
                FromAmount = GetDecimal(reader, "FromAmount"),
                ToAmount = GetDecimal(reader, "ToAmount"),
                Rate = GetDecimal(reader, "Rate")
            });

        EnsureBracketRows();
    }

    private void EnsureBracketRows()
    {
        while (Brackets.Count < 4)
        {
            Brackets.Add(new TaxBracketInput());
        }
    }

    private TaxSampleResult CalculateTax(TaxSampleInput input)
    {
        var annualIncome = input.AnnualTaxableIncomeBeforeAllowances;
        var allowance = 0m;

        if (input.IsWidowOrDivorced)
        {
            allowance += Setting.WidowOrDivorcedAllowance;
        }
        else
        {
            allowance += Setting.SelfAllowance;
        }

        if (input.HasHousewifeSpouse)
        {
            allowance += Setting.WifeHousewifeAllowance;
        }

        if (input.ChildrenCount > 0)
        {
            allowance += input.ChildrenCount * Setting.ChildAllowance;
        }

        if (input.Age >= Setting.AgeAllowanceFrom)
        {
            allowance += Setting.AgeAllowance;
        }

        allowance += input.OtherDeductions;

        var netTaxable = Math.Max(annualIncome - allowance, 0);
        var tax = 0m;

        foreach (var bracket in Brackets.OrderBy(x => x.FromAmount))
        {
            if (bracket.Rate <= 0 || netTaxable < bracket.FromAmount)
            {
                continue;
            }

            var start = bracket.FromAmount;
            var end = bracket.ToAmount <= 0 ? netTaxable : Math.Min(netTaxable, bracket.ToAmount);
            var taxableInBracket = Math.Max(end - start + 1, 0);

            if (taxableInBracket > 0)
            {
                tax += taxableInBracket * (bracket.Rate / 100m);
            }
        }

        return new TaxSampleResult
        {
            AnnualIncome = annualIncome,
            TotalAllowances = allowance,
            NetTaxableIncome = netTaxable,
            AnnualTax = Math.Round(tax, 0),
            MonthlyEstimatedTax = Math.Round(tax / 12m, 0)
        };
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

    public class TaxSettingInput
    {
        public bool IsEnabled { get; set; } = true;
        public string TaxBasis { get; set; } = "Annual";
        public decimal SelfAllowance { get; set; } = 5000000;
        public decimal WifeHousewifeAllowance { get; set; } = 4000000;
        public decimal ChildAllowance { get; set; } = 400000;
        public decimal WidowOrDivorcedAllowance { get; set; } = 6400000;
        public decimal AgeAllowance { get; set; } = 600000;
        public int AgeAllowanceFrom { get; set; } = 63;
        public decimal PrivateSectorAllowanceExemptPercent { get; set; } = 30;
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }

    public class TaxBracketInput
    {
        public decimal FromAmount { get; set; }
        public decimal ToAmount { get; set; }
        public decimal Rate { get; set; }
    }

    public class TaxSampleInput
    {
        public decimal AnnualTaxableIncomeBeforeAllowances { get; set; }
        public bool HasHousewifeSpouse { get; set; }
        public bool IsWidowOrDivorced { get; set; }
        public int ChildrenCount { get; set; }
        public int Age { get; set; }
        public decimal OtherDeductions { get; set; }
    }

    public class TaxSampleResult
    {
        public decimal AnnualIncome { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal NetTaxableIncome { get; set; }
        public decimal AnnualTax { get; set; }
        public decimal MonthlyEstimatedTax { get; set; }
    }
}
