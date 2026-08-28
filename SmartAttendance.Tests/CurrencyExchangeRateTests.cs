using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class CurrencyExchangeRateTests
{
    private static CurrencyExchangeRateStore.RateRow Rate(
        int id, string from, string to, DateOnly date, decimal value, bool active = true) => new()
    {
        Id = id, FromCurrency = from, ToCurrency = to, EffectiveDate = date, Rate = value, IsActive = active
    };

    [Fact]
    public void Resolve_UsesLatestEffectiveRateAndNeverAFutureRate()
    {
        var rows = new[]
        {
            Rate(1, "USD", "IQD", new DateOnly(2026, 1, 1), 1300m),
            Rate(2, "USD", "IQD", new DateOnly(2026, 6, 1), 1310m),
            Rate(3, "USD", "IQD", new DateOnly(2027, 1, 1), 1400m)
        };

        var resolved = Assert.IsType<CurrencyExchangeRateStore.ResolvedRate>(
            CurrencyExchangeRateStore.Resolve(rows, "usd", "iqd", new DateOnly(2026, 8, 31)));
        Assert.Equal(1310m, resolved.Rate);
        Assert.Equal(new DateOnly(2026, 6, 1), resolved.EffectiveDate);
        Assert.Equal(1_310_000m, resolved.Convert(1000m));
    }

    [Fact]
    public void Resolve_SupportsInverseAndIdentityButRejectsMissingPairs()
    {
        var rows = new[] { Rate(1, "USD", "IQD", new DateOnly(2026, 1, 1), 1250m) };

        var inverse = Assert.IsType<CurrencyExchangeRateStore.ResolvedRate>(
            CurrencyExchangeRateStore.Resolve(rows, "IQD", "USD", new DateOnly(2026, 2, 1)));
        Assert.True(inverse.IsInverse);
        Assert.Equal(0.0008m, inverse.Rate);
        Assert.Equal(1m, inverse.Convert(1250m));

        Assert.Equal(1m, CurrencyExchangeRateStore.Resolve(rows, "IQD", "IQD", new DateOnly(2026, 2, 1))!.Rate);
        Assert.Null(CurrencyExchangeRateStore.Resolve(rows, "EUR", "IQD", new DateOnly(2026, 2, 1)));
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" IQD ", "IQD")]
    [InlineData("US", null)]
    [InlineData("12A", null)]
    [InlineData("دول", null)]
    public void NormalizeCurrency_RequiresThreeLetters(string input, string? expected) =>
        Assert.Equal(expected, CurrencyExchangeRateStore.NormalizeCurrency(input));
}
