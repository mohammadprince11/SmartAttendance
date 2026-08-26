using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تسوية الإنهاء وبنود الفروقات.
///
/// **الأهمّ** <see cref="Difference_IsAlwaysPositive_WithDirectionSeparate"/>:
/// مبلغٌ سالب يُعرض بعلامتين («−500−» أو «(−500)») ويُقرأ خطأً بكل شاشة تعرضه،
/// فالمبلغ موجبٌ دائماً والاتجاه حقلٌ مستقل.
///
/// و<see cref="TerminationMonthUnpaid_WarnsWhenNoPayrollAtAll"/>: «لا مسير أصلاً»
/// حالةُ تنبيه لا حالةُ اطمئنان — وإلا خرجت أول تسوية بالنظام ناقصةَ شهر كامل.
/// </summary>
public class TerminationSettlementPolicyTests
{
    // ── الفروقات ───────────────────────────────────────────────────────────────

    [Fact]
    public void Difference_IsAlwaysPositive_WithDirectionSeparate()
    {
        var overWithheld = new TerminationSettlementPolicy.Difference("Tax", Withheld: 1200m, Due: 900m);
        var underWithheld = new TerminationSettlementPolicy.Difference("Tax", Withheld: 900m, Due: 1200m);

        Assert.Equal(300m, overWithheld.Amount);
        Assert.Equal(300m, underWithheld.Amount);
        Assert.True(overWithheld.IsRefund);
        Assert.False(underWithheld.IsRefund);
        Assert.Equal("يُردّ للموظف", overWithheld.DirectionLabel);
        Assert.Equal("يُستحقّ على الموظف", underWithheld.DirectionLabel);
    }

    [Fact]
    public void SignedAmount_AddsWhenRefunded_SubtractsWhenOwed()
    {
        var refund = new TerminationSettlementPolicy.Difference("Tax", 1200m, 900m);
        var owed = new TerminationSettlementPolicy.Difference("Gosi", 800m, 1000m);

        Assert.Equal(300m, refund.SignedAmount);
        Assert.Equal(-200m, owed.SignedAmount);
        Assert.Equal(100m, TerminationSettlementPolicy.NetDifference(new[] { refund, owed }));
    }

    [Fact]
    public void ZeroDifferences_AreExcluded_NotSummedAsZero()
    {
        var items = new[]
        {
            new TerminationSettlementPolicy.Difference("Tax", 1000m, 1000m),
            new TerminationSettlementPolicy.Difference("Gosi", 800m, 700m)
        };

        Assert.Single(TerminationSettlementPolicy.Material(items));
        Assert.Equal(100m, TerminationSettlementPolicy.NetDifference(items));
    }

    [Fact]
    public void Difference_RoundsToTwoDecimals()
    {
        var item = new TerminationSettlementPolicy.Difference("Tax", 1000.005m, 900m);

        Assert.Equal(100.01m, item.Amount);
    }

    // ── تنبيه شهر الإنهاء ──────────────────────────────────────────────────────

    [Fact]
    public void TerminationMonthUnpaid_WarnsWhenLastPayrollIsBefore()
    {
        var termination = new DateOnly(2026, 8, 15);
        var july = TerminationSettlementPolicy.MonthKey(2026, 7);
        var august = TerminationSettlementPolicy.MonthKey(2026, 8);

        Assert.True(TerminationSettlementPolicy.TerminationMonthUnpaid(termination, july));
        Assert.False(TerminationSettlementPolicy.TerminationMonthUnpaid(termination, august));
    }

    [Fact]
    public void TerminationMonthUnpaid_WarnsWhenNoPayrollAtAll()
    {
        // «لا مسير» حالة تنبيه لا اطمئنان.
        Assert.True(TerminationSettlementPolicy.TerminationMonthUnpaid(new DateOnly(2026, 8, 15), null));
    }

    [Fact]
    public void TerminationMonthUnpaid_HandlesYearBoundary()
    {
        var termination = new DateOnly(2027, 1, 5);

        Assert.True(TerminationSettlementPolicy.TerminationMonthUnpaid(
            termination, TerminationSettlementPolicy.MonthKey(2026, 12)));
        Assert.False(TerminationSettlementPolicy.TerminationMonthUnpaid(
            termination, TerminationSettlementPolicy.MonthKey(2027, 1)));
    }

    // ── المبلغ بالحروف ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "صفر دينار")]
    [InlineData(1, "واحد دينار")]
    [InlineData(3, "ثلاثة دينار")]
    [InlineData(11, "أحد عشر دينار")]
    [InlineData(21, "واحد وعشرون دينار")]
    [InlineData(25, "خمسة وعشرون دينار")]
    [InlineData(30, "ثلاثون دينار")]
    [InlineData(100, "مئة دينار")]
    [InlineData(215, "مئتان وخمسة عشر دينار")]
    [InlineData(1000, "ألف دينار")]
    [InlineData(2000, "ألفان دينار")]
    [InlineData(5000, "خمسة آلاف دينار")]
    [InlineData(12000, "اثنا عشر ألف دينار")]
    [InlineData(1000000, "مليون دينار")]
    public void AmountInWords_ReadsNaturally(int amount, string expected)
    {
        Assert.Equal(expected, TerminationSettlementPolicy.AmountInWords(amount));
    }

    [Fact]
    public void AmountInWords_IncludesFractionWhenPresent()
    {
        Assert.Equal("مئة دينار وخمسون فلس", TerminationSettlementPolicy.AmountInWords(100.50m));
        Assert.Equal("مئة دينار", TerminationSettlementPolicy.AmountInWords(100.00m));
    }

    [Fact]
    public void AmountInWords_MarksNegativeExplicitly()
    {
        // تسوية سالبة ممكنة (استُحقّ على الموظف أكثر ممّا له) — والوثيقة يجب أن تقولها.
        Assert.StartsWith("سالب", TerminationSettlementPolicy.AmountInWords(-250m));
    }

    [Fact]
    public void AmountInWords_ComposesLargeAmounts()
    {
        Assert.Equal(
            "مليون ومئتان وخمسون ألف دينار",
            TerminationSettlementPolicy.AmountInWords(1_250_000m));
    }

    [Fact]
    public void AmountInWords_UsesGivenCurrencyNames()
    {
        Assert.Equal("خمسة ريال", TerminationSettlementPolicy.AmountInWords(5m, "ريال", "هللة"));
    }

    [Theory]
    [InlineData("IQD", "دينار عراقي")]
    [InlineData("SAR", "ريال سعودي")]
    [InlineData("USD", "دولار أمريكي")]
    public void CurrencyAmountInWords_UsesThePayslipCurrency(string code, string expectedCurrency)
    {
        Assert.Contains(expectedCurrency, TerminationSettlementPolicy.CurrencyAmountInWords(250m, code));
    }

    // ── مدة الخدمة ─────────────────────────────────────────────────────────────

    [Fact]
    public void ServiceLength_BreaksIntoYearsMonthsDays()
    {
        Assert.Equal(
            "2 سنة و3 شهر و10 يوم",
            TerminationSettlementPolicy.ServiceLength(new DateOnly(2024, 1, 1), new DateOnly(2026, 4, 11)));
    }

    [Fact]
    public void ServiceLength_HandlesBorrowingAcrossMonths()
    {
        // 31 يناير ⟵ 1 مارس: شهرٌ واحد ويوم، لا شهران ناقص يوم.
        var length = TerminationSettlementPolicy.ServiceLength(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1));

        Assert.Contains("1 شهر", length);
    }

    [Fact]
    public void ServiceLength_SameDayIsZero()
    {
        Assert.Equal("0 يوم", TerminationSettlementPolicy.ServiceLength(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void ServiceLength_RejectsInvertedRange()
    {
        Assert.Equal("0", TerminationSettlementPolicy.ServiceLength(new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1)));
    }
}
