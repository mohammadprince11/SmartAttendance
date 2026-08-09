using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// السيناريوهان السادس (إجازة غير مدفوعة) والسابع (أوفرتايم) من الموظف الذهبي،
/// مع تثبيت التوافق الخلفيّ: النمط الافتراضي «الأساسي» + مقام 30 + 8 ساعات يعيد
/// أرقام المحرك القائم حرفياً.
/// </summary>
public class PayrollBasePolicyTests
{
    [Fact]
    public void DefaultBasis_Is30And8_BackwardCompatible()
    {
        Assert.Equal(30m, PayrollDivisorPolicy.Divisor(null, 31));
        Assert.Equal(8m, PayrollDivisorPolicy.DailyHours(null));
        Assert.Equal(PayrollEarningBase.ModeBasic, PayrollEarningBase.NormalizeMode(null));
    }

    [Fact]
    public void PeriodDaysBasis_UsesActualDays()
    {
        Assert.Equal(28m, PayrollDivisorPolicy.Divisor(PayrollDivisorPolicy.BasisPeriodDays, 28));
        Assert.Equal(31m, PayrollDivisorPolicy.Divisor(PayrollDivisorPolicy.BasisPeriodDays, 31));
    }

    [Fact]
    public void UnpaidLeave_BasicOnly_LegacyNumber()
    {
        // الافتراض: الأساسي وحده ÷ 30 × يوم = 13,333.33 (سلوك سابق).
        var ulBase = PayrollEarningBase.Compose(PayrollEarningBase.ModeBasic, 400_000m, 1_600_000m);
        var daily = PayrollRateBasis.DailyRate(ulBase, PayrollDivisorPolicy.Divisor(null, 30));

        Assert.Equal(400_000m, ulBase);
        Assert.Equal(13_333.33m, decimal.Round(1 * daily, 2));
    }

    [Fact]
    public void UnpaidLeave_BasicPlusAllowances_ScenarioSix()
    {
        // السيناريو السادس: الوعاء 2,000,000 ÷ 30 × يوم = 66,666.67 (ليس 400,000/30).
        var ulBase = PayrollEarningBase.Compose(
            PayrollEarningBase.ModeBasicPlusAllowances, 400_000m, 1_600_000m);
        var daily = PayrollRateBasis.DailyRate(ulBase, PayrollDivisorPolicy.Divisor(null, 30));

        Assert.Equal(2_000_000m, ulBase);
        Assert.Equal(66_666.67m, decimal.Round(1 * daily, 2));
    }

    [Fact]
    public void Overtime_BasicBase_ScenarioSeven()
    {
        // السيناريو السابع: OvertimeBase=Basic 400,000، مقام 30، 8 ساعات، 8س × 1.5 = 20,000.
        var otBase = PayrollEarningBase.Compose(PayrollEarningBase.ModeBasic, 400_000m, 1_600_000m);
        var daily = PayrollRateBasis.DailyRate(otBase, PayrollDivisorPolicy.Divisor(null, 30));
        var hourly = PayrollRateBasis.HourlyRate(daily, PayrollDivisorPolicy.DailyHours(null));

        var amount = decimal.Round(8m * hourly * 1.5m, 2);
        Assert.Equal(20_000m, amount);
    }

    [Fact]
    public void Overtime_BasicPlusAllowances_DiffersFromBasicOnly()
    {
        // نفس المدخلات لكن الوعاء = أساسي + علاوات مؤهَّلة ⟹ نتيجة مختلفة.
        var otBase = PayrollEarningBase.Compose(
            PayrollEarningBase.ModeBasicPlusAllowances, 400_000m, 1_600_000m);
        var daily = PayrollRateBasis.DailyRate(otBase, PayrollDivisorPolicy.Divisor(null, 30));
        var hourly = PayrollRateBasis.HourlyRate(daily, PayrollDivisorPolicy.DailyHours(null));

        var amount = decimal.Round(8m * hourly * 1.5m, 2);
        Assert.Equal(100_000m, amount);   // 2,000,000/30/8 × 8 × 1.5
        Assert.NotEqual(20_000m, amount);
    }
}
