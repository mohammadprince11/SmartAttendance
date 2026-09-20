using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public class EndOfServiceStoreTests
{
    private const decimal MonthlyBasis = 600_000m;

    [Fact]
    public void DefaultPolicy_IsFailClosed()
    {
        Assert.False(EndOfServicePolicy.Policy.Default.AutoCalculationEnabled);
        Assert.Equal(2m, EndOfServicePolicy.Policy.Default.WeeksPerYear);
    }

    [Fact]
    public void ComputeGratuity_TwoWeeksPerYear_UsesThirtyDayMonth()
    {
        var (amount, breakdown) =
            EndOfServiceStore.ComputeGratuity(3m, MonthlyBasis, 2m);

        // 600,000 / 30 * 14 days * 3 years
        Assert.Equal(840_000m, amount);
        Assert.Contains("2", breakdown);
        Assert.Contains("أسبوع", breakdown);
    }

    [Fact]
    public void ComputeGratuity_DoubleMultiplier_IsExplicit()
    {
        var normal = EndOfServiceStore.ComputeGratuity(5m, MonthlyBasis, 2m, 1m).Gratuity;
        var doubled = EndOfServiceStore.ComputeGratuity(5m, MonthlyBasis, 2m, 2m).Gratuity;

        Assert.Equal(normal * 2m, doubled);
    }

    [Theory]
    [InlineData(0, 600000, 2, 1)]
    [InlineData(3, 0, 2, 1)]
    [InlineData(3, 600000, 0, 1)]
    [InlineData(3, 600000, 2, 0)]
    public void ComputeGratuity_InvalidOrIneligibleInputs_ReturnZero(
        double years, double basis, double weeks, double multiplier)
    {
        var amount = EndOfServiceStore.ComputeGratuity(
            (decimal)years, (decimal)basis, (decimal)weeks, (decimal)multiplier).Gratuity;

        Assert.Equal(0m, amount);
    }

    [Fact]
    public void Policy_NormalizesWeeksToSafeRange()
    {
        Assert.Equal(0m, new EndOfServicePolicy.Policy(true, -1m).Normalized().WeeksPerYear);
        Assert.Equal(52m, new EndOfServicePolicy.Policy(true, 90m).Normalized().WeeksPerYear);
    }

    [Fact]
    public void YearsOfService_EndBeforeOrEqualStart_IsZero()
    {
        var d = new DateOnly(2020, 6, 1);
        Assert.Equal(0m, EndOfServiceStore.YearsOfService(d, d));
        Assert.Equal(0m, EndOfServiceStore.YearsOfService(d, new DateOnly(2019, 1, 1)));
    }

    [Fact]
    public void YearsOfService_TenFullYears_IsAboutTen()
    {
        var years = EndOfServiceStore.YearsOfService(
            new DateOnly(2015, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Equal(10.0m, years);
    }

    [Fact]
    public void YearsOfService_HalfYear_IsAboutHalf()
    {
        var years = EndOfServiceStore.YearsOfService(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 7, 1));

        Assert.InRange(years, 0.49m, 0.51m);
    }

    [Fact]
    public void YearsOfService_FeedsConfiguredGratuity()
    {
        var years = EndOfServiceStore.YearsOfService(
            new DateOnly(2018, 1, 1), new DateOnly(2026, 1, 1));

        var amount = EndOfServiceStore.ComputeGratuity(
            years, MonthlyBasis, 2m).Gratuity;

        Assert.InRange(years, 7.9m, 8.1m);
        Assert.True(amount > 0m);
    }
}
