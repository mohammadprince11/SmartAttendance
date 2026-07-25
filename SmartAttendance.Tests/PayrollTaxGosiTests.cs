using System.Collections.Generic;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية لحاسبتي الضريبة والضمان في <see cref="PayrollConfigStore"/> — بلا قاعدة بيانات.
/// الضريبة تصاعدية بالشرائح على (الخاضع − الإعفاء)؛ الضمان حصتا موظف/شركة مع سقف اختياري.
/// عيبٌ هنا = اقتطاع ضريبي/ضماني خاطئ يظهر في كل قسيمة راتب.
/// </summary>
public class PayrollTaxGosiTests
{
    /// <summary>ملف ضريبي بثلاث شرائح تصاعدية: 0–100k @10%، 100k–200k @20%، 200k+ @30%.</summary>
    private static PayrollConfigStore.TaxProfile TaxProfile(decimal exemption = 0m) => new()
    {
        ExemptionAmount = exemption,
        Brackets = new List<PayrollConfigStore.TaxBracket>
        {
            new() { FromAmount = 0m,      ToAmount = 100_000m, Rate = 10m },
            new() { FromAmount = 100_000m, ToAmount = 200_000m, Rate = 20m },
            new() { FromAmount = 200_000m, ToAmount = null,     Rate = 30m }
        }
    };

    // ---------------- ComputeTax ----------------

    [Fact]
    public void ComputeTax_NullProfile_IsZero()
    {
        Assert.Equal(0m, PayrollConfigStore.ComputeTax(500_000m, null));
    }

    [Fact]
    public void ComputeTax_NoBrackets_IsZero()
    {
        var profile = new PayrollConfigStore.TaxProfile();
        Assert.Equal(0m, PayrollConfigStore.ComputeTax(500_000m, profile));
    }

    [Fact]
    public void ComputeTax_WithinFirstBracket_SingleRate()
    {
        Assert.Equal(5_000m, PayrollConfigStore.ComputeTax(50_000m, TaxProfile())); // 50k × 10%
    }

    [Fact]
    public void ComputeTax_SpansTwoBrackets_IsProgressive()
    {
        // 150k = (100k × 10%) + (50k × 20%) = 10,000 + 10,000
        Assert.Equal(20_000m, PayrollConfigStore.ComputeTax(150_000m, TaxProfile()));
    }

    [Fact]
    public void ComputeTax_SpansAllBrackets_IncludingOpenTop()
    {
        // 300k = (100k×10%) + (100k×20%) + (100k×30%) = 10k + 20k + 30k
        Assert.Equal(60_000m, PayrollConfigStore.ComputeTax(300_000m, TaxProfile()));
    }

    [Fact]
    public void ComputeTax_SubtractsExemptionFirst()
    {
        // خاضع 400k − إعفاء 100k = وعاء 300k ⟹ 60k (نفس الحالة أعلاه)
        Assert.Equal(60_000m, PayrollConfigStore.ComputeTax(400_000m, TaxProfile(exemption: 100_000m)));
    }

    [Fact]
    public void ComputeTax_TaxableBelowExemption_IsZero()
    {
        Assert.Equal(0m, PayrollConfigStore.ComputeTax(80_000m, TaxProfile(exemption: 100_000m)));
    }

    [Fact]
    public void ComputeTax_ExactlyAtExemption_IsZero()
    {
        Assert.Equal(0m, PayrollConfigStore.ComputeTax(100_000m, TaxProfile(exemption: 100_000m)));
    }

    [Fact]
    public void ComputeTax_IsMonotonicWithIncome()
    {
        var p = TaxProfile();
        var low = PayrollConfigStore.ComputeTax(120_000m, p);
        var mid = PayrollConfigStore.ComputeTax(250_000m, p);
        var high = PayrollConfigStore.ComputeTax(500_000m, p);
        Assert.True(low < mid && mid < high); // دخل أعلى ⟹ ضريبة أعلى أبداً
    }

    // ---------------- ComputeGosi ----------------

    /// <summary>ملف ضمان عراقي نموذجي: حصة الموظف 5%، حصة الشركة 12%.</summary>
    private static PayrollConfigStore.GosiProfile GosiProfile(decimal ceiling = 0m) => new()
    {
        EmployeeRate = 5m,
        CompanyRate = 12m,
        Ceiling = ceiling
    };

    [Fact]
    public void ComputeGosi_NullProfile_IsZeroZero()
    {
        Assert.Equal((0m, 0m), PayrollConfigStore.ComputeGosi(1_000_000m, null));
    }

    [Fact]
    public void ComputeGosi_NoCeiling_UsesFullGross()
    {
        var (emp, co) = PayrollConfigStore.ComputeGosi(1_000_000m, GosiProfile());
        Assert.Equal(50_000m, emp);   // 5%
        Assert.Equal(120_000m, co);   // 12%
    }

    [Fact]
    public void ComputeGosi_AboveCeiling_CapsBasisAtCeiling()
    {
        var (emp, co) = PayrollConfigStore.ComputeGosi(1_000_000m, GosiProfile(ceiling: 800_000m));
        Assert.Equal(40_000m, emp);   // 5% من 800k لا من المليون
        Assert.Equal(96_000m, co);    // 12% من 800k
    }

    [Fact]
    public void ComputeGosi_BelowCeiling_UsesGross()
    {
        var (emp, co) = PayrollConfigStore.ComputeGosi(500_000m, GosiProfile(ceiling: 800_000m));
        Assert.Equal(25_000m, emp);
        Assert.Equal(60_000m, co);
    }

    [Fact]
    public void ComputeGosi_ZeroOrNegativeGross_IsZeroZero()
    {
        Assert.Equal((0m, 0m), PayrollConfigStore.ComputeGosi(0m, GosiProfile()));
        Assert.Equal((0m, 0m), PayrollConfigStore.ComputeGosi(-100m, GosiProfile()));
    }

    [Fact]
    public void ComputeGosi_CompanyShareExceedsEmployeeShare_ForIraqiRates()
    {
        var (emp, co) = PayrollConfigStore.ComputeGosi(1_000_000m, GosiProfile());
        Assert.True(co > emp); // 12% > 5%
    }
}
