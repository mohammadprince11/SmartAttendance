using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس تهيئة المسير (Phase 24): المدخل الفاسد يُرفض لا يُنتج راتباً خاطئاً.
/// </summary>
public class PayrollConfigValidationTests
{
    [Fact]
    public void Gosi_ValidRates_NoErrors()
    {
        Assert.Empty(PayrollConfigValidation.ValidateGosi(5m, 12m, 0m));
    }

    [Theory]
    [InlineData(-1, 12, 0)]   // نسبة موظف سالبة
    [InlineData(5, -1, 0)]    // نسبة شركة سالبة
    [InlineData(101, 12, 0)]  // > 100
    [InlineData(5, 12, -5)]   // سقف سالب
    public void Gosi_InvalidRates_Rejected(double emp, double co, double ceiling)
    {
        Assert.NotEmpty(PayrollConfigValidation.ValidateGosi((decimal)emp, (decimal)co, (decimal)ceiling));
    }

    [Fact]
    public void Tax_ValidAscendingBrackets_NoErrors()
    {
        var brackets = new (decimal, decimal?, decimal)[]
        {
            (0m, 250_000m, 3m),
            (250_000m, 500_000m, 5m),
            (500_000m, null, 10m),
        };
        Assert.Empty(PayrollConfigValidation.ValidateTax(250_000m, brackets));
    }

    [Fact]
    public void Tax_OverlappingBrackets_Rejected()
    {
        // الثانية تبدأ 200,000 قبل نهاية الأولى 250,000 — تداخل.
        var brackets = new (decimal, decimal?, decimal)[]
        {
            (0m, 250_000m, 3m),
            (200_000m, 500_000m, 5m),
        };
        Assert.NotEmpty(PayrollConfigValidation.ValidateTax(0m, brackets));
    }

    [Fact]
    public void Tax_OpenEndedNotLast_Rejected()
    {
        var brackets = new (decimal, decimal?, decimal)[]
        {
            (0m, null, 3m),           // مفتوحة لكنها ليست الأخيرة
            (500_000m, null, 5m),
        };
        Assert.NotEmpty(PayrollConfigValidation.ValidateTax(0m, brackets));
    }

    [Fact]
    public void Tax_NegativeExemptionOrRate_Rejected()
    {
        Assert.NotEmpty(PayrollConfigValidation.ValidateTax(-1m, System.Array.Empty<(decimal, decimal?, decimal)>()));
        Assert.NotEmpty(PayrollConfigValidation.ValidateTax(0m, new (decimal, decimal?, decimal)[] { (0m, 100m, 150m) }));
    }

    [Fact]
    public void Tax_BracketEndBeforeStart_Rejected()
    {
        Assert.NotEmpty(PayrollConfigValidation.ValidateTax(0m, new (decimal, decimal?, decimal)[] { (500_000m, 250_000m, 5m) }));
    }

    [Fact]
    public void EmployeeSalaries_Negative_Rejected()
    {
        Assert.NotEmpty(PayrollConfigValidation.ValidateEmployeeSalaries(-1m, null, null));
        Assert.NotEmpty(PayrollConfigValidation.ValidateEmployeeSalaries(null, -1m, null));
        Assert.NotEmpty(PayrollConfigValidation.ValidateEmployeeSalaries(null, null, -1m));
        Assert.Empty(PayrollConfigValidation.ValidateEmployeeSalaries(400_000m, 400_000m, 400_000m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-8)]
    [InlineData(25)]
    public void DailyHours_OutOfRange_Rejected(double hours)
    {
        Assert.NotEmpty(PayrollConfigValidation.ValidateStandardDailyHours((decimal)hours));
    }

    [Fact]
    public void DailyHours_Valid_NoErrors()
    {
        Assert.Empty(PayrollConfigValidation.ValidateStandardDailyHours(8m));
    }
}
