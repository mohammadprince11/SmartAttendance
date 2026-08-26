using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class PayrollAdjustmentRunContractTests
{
    [Theory]
    [InlineData("Regular", "Regular")]
    [InlineData("offcycle", "OffCycle")]
    [InlineData("RETROACTIVE", "Retroactive")]
    [InlineData("Reversal", "Reversal")]
    [InlineData("unknown", "Regular")]
    public void Run_type_is_normalized_to_closed_catalog(string input, string expected) =>
        Assert.Equal(expected, PayrollRunStore.NormalizeRunType(input));

    [Theory]
    [InlineData("2026-8-N-1", "2026-8-1")]
    [InlineData("2026-8-O-2", "2026-8-2")]
    [InlineData("2026-8-R-3", "2026-8-3")]
    [InlineData("2026-8-V-4", "2026-8-4")]
    [InlineData("legacy-17", "legacy-17")]
    [InlineData("", "—")]
    public void Technical_run_type_code_is_hidden_from_user_facing_batch_number(string input, string expected) =>
        Assert.Equal(expected, PayrollRunStore.DisplayBatchNumber(input));

    [Fact]
    public void Payroll_action_menu_has_no_fill_until_hover()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRoot(), "SmartAttendance.Web", "wwwroot", "css", "pages", "runs-0e69354927.css"));

        Assert.Contains("background:transparent !important", css);
        Assert.Contains(".zy-scope.zy-ui-contract .pr-menu-panel .pr-mi:hover", css);
        Assert.Contains("background:var(--zy-migrated-color-c201d2dab2) !important", css);

        var page = File.ReadAllText(Path.Combine(
            FindRoot(), "SmartAttendance.Web", "Pages", "Payroll", "Runs.cshtml"));
        Assert.Contains("class=\"pr-menu-panel\" role=\"menu\" data-zy-preserve", page);
    }

    [Fact]
    public void Negative_or_zero_bank_settlements_are_not_payable()
    {
        Assert.False(new PayrollRunStore.BankFileRow { Iban = "IQ00", NetSalary = -10 }.IsPayable);
        Assert.False(new PayrollRunStore.BankFileRow { Iban = "IQ00", NetSalary = 0 }.IsPayable);
        Assert.True(new PayrollRunStore.BankFileRow { Iban = "IQ00", NetSalary = 10 }.IsPayable);
    }

    [Fact]
    public void Adjustment_engine_never_enters_regular_salary_calculation()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));
        Assert.Contains("if (run.RunType != RunTypeRegular)", source);
        Assert.Contains("CalculateAdjustmentRunAsync", source);
        Assert.Contains("لا أساسي ولا علاوات دورية ولا أثر حضور", source);
        Assert.Contains("RunTypeOffCycle => \"ISNULL(t.IsRetroactive,0)=0", source);
        Assert.Contains("RunTypeRetroactive => \"ISNULL(t.IsRetroactive,0)=1\"", source);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
