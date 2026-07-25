using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية لحاسبة مكافأة نهاية الخدمة <see cref="EndOfServiceStore"/> — بلا قاعدة بيانات.
/// الشرائح الافتراضية: أول 5 سنوات = نصف شهر/سنة، وما بعدها = شهر/سنة، على آخر أساسي.
/// عيبٌ هنا = تسوية نهاية خدمة خاطئة (مبلغ يُدفع فعلياً للموظف).
/// </summary>
public class EndOfServiceStoreTests
{
    private const decimal Basic = 600_000m;

    // ---------------- ComputeGratuity: الشرائح ----------------

    [Fact]
    public void ComputeGratuity_ZeroYears_IsZeroWithDash()
    {
        var (amount, breakdown) = EndOfServiceStore.ComputeGratuity(0m, Basic);
        Assert.Equal(0m, amount);
        Assert.Equal("—", breakdown);
    }

    [Fact]
    public void ComputeGratuity_WithinFirstTier_HalfMonthPerYear()
    {
        // 3 سنوات × نصف شهر × الأساسي
        var (amount, _) = EndOfServiceStore.ComputeGratuity(3m, Basic);
        Assert.Equal(3m * 0.5m * Basic, amount); // 900,000
    }

    [Fact]
    public void ComputeGratuity_ExactlyFiveYears_StaysInFirstTierOnly()
    {
        var (amount, _) = EndOfServiceStore.ComputeGratuity(5m, Basic);
        Assert.Equal(5m * 0.5m * Basic, amount); // 1,500,000 — لا يعبر للشريحة الثانية عند الحد تماماً
    }

    [Fact]
    public void ComputeGratuity_AboveFiveYears_CrossesBothTiers()
    {
        // 7 سنوات = (5 × 0.5) + (2 × 1.0) شهور × الأساسي
        var (amount, breakdown) = EndOfServiceStore.ComputeGratuity(7m, Basic);
        var expected = (5m * 0.5m * Basic) + (2m * 1.0m * Basic); // 1,500,000 + 1,200,000 = 2,700,000
        Assert.Equal(expected, amount);
        Assert.Contains("+", breakdown); // شريحتان في الوصف
    }

    [Fact]
    public void ComputeGratuity_FractionalYears_AreProrated()
    {
        // 2.5 سنة × نصف شهر × الأساسي
        var (amount, _) = EndOfServiceStore.ComputeGratuity(2.5m, Basic);
        Assert.Equal(System.Math.Round(2.5m * 0.5m * Basic, 2), amount); // 750,000
    }

    [Fact]
    public void ComputeGratuity_ZeroBasic_IsZero()
    {
        var (amount, _) = EndOfServiceStore.ComputeGratuity(10m, 0m);
        Assert.Equal(0m, amount);
    }

    [Fact]
    public void ComputeGratuity_MonotonicallyIncreasesWithService()
    {
        var a3 = EndOfServiceStore.ComputeGratuity(3m, Basic).Gratuity;
        var a5 = EndOfServiceStore.ComputeGratuity(5m, Basic).Gratuity;
        var a8 = EndOfServiceStore.ComputeGratuity(8m, Basic).Gratuity;
        Assert.True(a3 < a5 && a5 < a8); // خدمة أطول ⟹ مكافأة أكبر أبداً
    }

    [Fact]
    public void ComputeGratuity_BreakdownDescribesTiers()
    {
        var (_, breakdown) = EndOfServiceStore.ComputeGratuity(3m, Basic);
        Assert.Contains("3", breakdown);
        Assert.Contains("شهر", breakdown);
    }

    // ---------------- YearsOfService ----------------

    [Fact]
    public void YearsOfService_EndBeforeOrEqualStart_IsZero()
    {
        var d = new System.DateOnly(2020, 6, 1);
        Assert.Equal(0m, EndOfServiceStore.YearsOfService(d, d));                       // نفس اليوم
        Assert.Equal(0m, EndOfServiceStore.YearsOfService(d, new System.DateOnly(2019, 1, 1))); // النهاية قبل البداية
    }

    [Fact]
    public void YearsOfService_TenFullYears_IsAboutTen()
    {
        var years = EndOfServiceStore.YearsOfService(new System.DateOnly(2015, 1, 1), new System.DateOnly(2025, 1, 1));
        Assert.Equal(10.0m, years); // 3653 يوماً / 365.25 ≈ 10.00
    }

    [Fact]
    public void YearsOfService_HalfYear_IsAboutHalf()
    {
        var years = EndOfServiceStore.YearsOfService(new System.DateOnly(2024, 1, 1), new System.DateOnly(2024, 7, 1));
        Assert.InRange(years, 0.49m, 0.51m); // ~182 يوماً / 365.25 ≈ 0.50
    }

    [Fact]
    public void YearsOfService_FeedsGratuity_EndToEnd()
    {
        // تكامل الدالتين: تعيين 2018-01-01 وترك 2026-01-01 ≈ 8 سنوات ⟹ يعبر الشريحتين.
        var years = EndOfServiceStore.YearsOfService(new System.DateOnly(2018, 1, 1), new System.DateOnly(2026, 1, 1));
        Assert.InRange(years, 7.9m, 8.1m);
        var (amount, _) = EndOfServiceStore.ComputeGratuity(years, Basic);
        Assert.True(amount > 5m * 0.5m * Basic); // أكبر من سقف الشريحة الأولى وحدها
    }
}
