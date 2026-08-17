using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// الحدود القصوى الشهرية — الاختبارات تثبت قبل كل شيء أن **الافتراضي بلا أثر**
/// (قاعدة الرواتب الحمراء)، ثم سلوك الأضيق-يفوز والترحيل المُعلَن.
/// </summary>
public class PayrollCapsPolicyTests
{
    [Fact]
    public void DefaultCaps_HaveNoEffect()
    {
        var (applied, deferred) = PayrollCapsPolicy.ApplyDeductionCap(PayrollCapsPolicy.Caps.None, 5000m, 1234.56m);
        Assert.Equal(1234.56m, applied);
        Assert.Equal(0m, deferred);

        var (h, a, trimmed) = PayrollCapsPolicy.ApplyOvertimeCap(PayrollCapsPolicy.Caps.None, 40m, 800m);
        Assert.Equal(40m, h);
        Assert.Equal(800m, a);
        Assert.Equal(0m, trimmed);
    }

    [Fact]
    public void DeductionCap_ByAmount_DefersExcess()
    {
        var caps = new PayrollCapsPolicy.Caps(1000m, 0, 0, 0);
        var (applied, deferred) = PayrollCapsPolicy.ApplyDeductionCap(caps, 5000m, 1500m);
        Assert.Equal(1000m, applied);
        Assert.Equal(500m, deferred);
    }

    [Fact]
    public void DeductionCap_ByPercent_UsesGross()
    {
        var caps = new PayrollCapsPolicy.Caps(0, 25m, 0, 0); // 25% من الإجمالي
        Assert.Equal(1250m, PayrollCapsPolicy.EffectiveDeductionCap(caps, 5000m));
        var (applied, deferred) = PayrollCapsPolicy.ApplyDeductionCap(caps, 5000m, 2000m);
        Assert.Equal(1250m, applied);
        Assert.Equal(750m, deferred);
    }

    [Fact]
    public void DeductionCap_TightestWins()
    {
        var caps = new PayrollCapsPolicy.Caps(1000m, 50m, 0, 0); // 1000 مقابل 50%×5000=2500 ⟹ 1000
        Assert.Equal(1000m, PayrollCapsPolicy.EffectiveDeductionCap(caps, 5000m));

        var caps2 = new PayrollCapsPolicy.Caps(3000m, 10m, 0, 0); // 3000 مقابل 500 ⟹ 500
        Assert.Equal(500m, PayrollCapsPolicy.EffectiveDeductionCap(caps2, 5000m));
    }

    [Fact]
    public void DeductionCap_BelowCap_Untouched()
    {
        var caps = new PayrollCapsPolicy.Caps(1000m, 0, 0, 0);
        var (applied, deferred) = PayrollCapsPolicy.ApplyDeductionCap(caps, 5000m, 800m);
        Assert.Equal(800m, applied);
        Assert.Equal(0m, deferred);
    }

    [Fact]
    public void OvertimeCap_ByHours_KeepsHourlyRate()
    {
        var caps = new PayrollCapsPolicy.Caps(0, 0, 0, 30m);
        var (h, a, trimmed) = PayrollCapsPolicy.ApplyOvertimeCap(caps, 40m, 800m); // 20/ساعة
        Assert.Equal(30m, h);
        Assert.Equal(600m, a);
        Assert.Equal(10m, trimmed);
    }

    [Fact]
    public void OvertimeCap_ByAmount_TrimsHours()
    {
        var caps = new PayrollCapsPolicy.Caps(0, 0, 500m, 0);
        var (h, a, trimmed) = PayrollCapsPolicy.ApplyOvertimeCap(caps, 40m, 800m); // 20/ساعة
        Assert.Equal(500m, a);
        Assert.Equal(25m, h);
        Assert.Equal(15m, trimmed);
    }

    [Fact]
    public void OvertimeCap_BothCaps_TightestWins()
    {
        var caps = new PayrollCapsPolicy.Caps(0, 0, 500m, 30m); // ساعات ⟹ 600 ثم مبلغ ⟹ 500
        var (h, a, _) = PayrollCapsPolicy.ApplyOvertimeCap(caps, 40m, 800m);
        Assert.Equal(500m, a);
        Assert.Equal(25m, h);
    }

    [Theory]
    [InlineData("abc", "-5", "", null)]
    [InlineData("0", "0", "0", "0")]
    public void Parse_InvalidOrZero_MeansNoCap(string? d1, string? d2, string? o1, string? o2)
    {
        var caps = PayrollCapsPolicy.Parse(d1, d2, o1, o2);
        Assert.False(caps.HasDeductionCap);
        Assert.False(caps.HasOvertimeCap);
    }

    [Fact]
    public void Parse_ValidValues()
    {
        var caps = PayrollCapsPolicy.Parse("1500.5", "30", "900", "40");
        Assert.Equal(1500.5m, caps.DeductionCapAmount);
        Assert.Equal(30m, caps.DeductionCapPercentOfGross);
        Assert.Equal(900m, caps.OvertimeCapAmount);
        Assert.Equal(40m, caps.OvertimeCapHours);
    }
}
