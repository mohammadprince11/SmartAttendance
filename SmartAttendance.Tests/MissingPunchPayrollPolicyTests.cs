using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class MissingPunchPayrollPolicyTests
{
    [Theory]
    [InlineData("25", 25)]
    [InlineData("150", 100)]
    [InlineData("-5", 0)]
    [InlineData("bad", 0)]
    public void Percent_is_safely_normalized(string raw, decimal expected) =>
        Assert.Equal(expected, MissingPunchPayrollPolicy.ParsePercent(raw));

    [Fact]
    public void Penalty_is_percent_of_daily_basic_per_incomplete_day()
    {
        var result = MissingPunchPayrollPolicy.Calculate(
            dailyBasic: 100_000m,
            incompleteDays: 2,
            percent: 25m);

        Assert.Equal(50_000m, result);
    }

    [Fact]
    public void No_incomplete_days_means_no_penalty() =>
        Assert.Equal(0m, MissingPunchPayrollPolicy.Calculate(100_000m, 0, 25m));
}
