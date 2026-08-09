using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// سياسة التقريب (Phase 25): يُحسَب كل شيء بـ<c>decimal</c> (لا <c>double</c>)،
/// وتُقرَّب المبالغ لخانتين والأجور (يوميّ/ساعيّ) لأربع، بتقريبٍ مصرفيّ (ToEven)
/// موحَّد عبر كل الدوال. تُثبِّت هذه الاختبارات الحوافّ كي لا يتسلّل تقريبٌ مختلط.
/// </summary>
public class PayrollRoundingTests
{
    [Fact]
    public void Money_RoundsToTwoDecimals_BankersRounding()
    {
        // 100.125 → 100.12 (لأقرب زوجيّ)، 100.135 → 100.14 — سياسة ToEven الموحّدة.
        Assert.Equal(100.12m, decimal.Round(100.125m, 2, System.MidpointRounding.ToEven));
        Assert.Equal(100.14m, decimal.Round(100.135m, 2, System.MidpointRounding.ToEven));
    }

    [Fact]
    public void AttendanceAdjust_RoundsEachComponentToTwoDecimals()
    {
        // 1,600,000 × 24/26 = 1,476,923.0769… → 1,476,923.08
        var comp = new AttendanceSalaryBase.EarningComponent(1_600_000m, true);
        Assert.Equal(1_476_923.08m, AttendanceSalaryBase.AdjustComponent(comp, 24m / 26m));
    }

    [Fact]
    public void RateBasis_DailyAndHourly_RoundToFourDecimals()
    {
        // 400,000 ÷ 30 = 13,333.3333 · ÷ 8 = 1,666.6667 — أربع خانات ثابتة.
        var daily = PayrollRateBasis.DailyRate(400_000m, 30m);
        var hourly = PayrollRateBasis.HourlyRate(daily, 8m);
        Assert.Equal(13_333.3333m, daily);
        Assert.Equal(1_666.6667m, hourly);
    }

    [Fact]
    public void RateBasis_ZeroDivisorOrHours_YieldsZero_NotThrow()
    {
        Assert.Equal(0m, PayrollRateBasis.DailyRate(400_000m, 0m));
        Assert.Equal(0m, PayrollRateBasis.HourlyRate(1_000m, 0m));
    }

    [Fact]
    public void Gosi_RoundsSharesToTwoDecimals()
    {
        // 333,333 × 5% = 16,666.65 · × 12% = 39,999.96 — كلٌّ لخانتين.
        var (emp, co) = PayrollConfigStore.ComputeGosi(
            333_333m, new PayrollConfigStore.GosiProfile { EmployeeRate = 5m, CompanyRate = 12m });
        Assert.Equal(16_666.65m, emp);
        Assert.Equal(39_999.96m, co);
    }

    [Fact]
    public void Tax_RoundsToTwoDecimals()
    {
        // خاضع 100,001 بشريحة 3% = 3,000.03 — لخانتين.
        var profile = new PayrollConfigStore.TaxProfile
        {
            ExemptionAmount = 0m,
            Brackets = { new PayrollConfigStore.TaxBracket { FromAmount = 0m, ToAmount = null, Rate = 3m } }
        };
        Assert.Equal(3_000.03m, PayrollConfigStore.ComputeTax(100_001m, profile));
    }

    [Fact]
    public void Compose_RoundsSumToTwoDecimals()
    {
        var amounts = new SalaryBaseComposer.Amounts { Basic = 100.005m, Allowances = 0.001m };
        // 100.006 → 100.01 لخانتين.
        Assert.Equal(100.01m, SalaryBaseComposer.Compose(amounts, new[] { "Basic", "Allowances" }));
    }
}
