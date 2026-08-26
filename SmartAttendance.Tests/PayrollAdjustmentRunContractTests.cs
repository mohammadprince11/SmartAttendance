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
