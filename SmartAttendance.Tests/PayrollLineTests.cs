using System.Linq;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية للخصائص المحسوبة في <see cref="PayrollRunStore"/> — بلا قاعدة بيانات.
/// إجماليات القسيمة (استقطاعات، كلفة صاحب العمل)، تقسيم البنود لإضافات/استقطاعات،
/// وتسميات دورة الحياة العربية. عيبٌ هنا = قسيمة راتب أو تقرير كلفة خاطئ.
/// </summary>
public class PayrollLineTests
{
    private static PayrollRunStore.PayrollLine Line() => new()
    {
        GrossSalary = 1_000_000m,
        TaxAmount = 50_000m,
        GosiEmployee = 50_000m,   // 5% حصة الموظف
        GosiCompany = 120_000m,   // 12% حصة الشركة
        OtherDeductions = 30_000m
    };

    [Fact]
    public void TotalDeductions_SumsTaxGosiEmployeeAndOther()
    {
        Assert.Equal(130_000m, Line().TotalDeductions); // 50k + 50k + 30k
    }

    [Fact]
    public void EmployerCost_IsGrossPlusCompanyGosi()
    {
        Assert.Equal(1_120_000m, Line().EmployerCost); // الإجمالي + حصة الشركة (لا تُخصم من الموظف)
    }

    [Fact]
    public void TotalDeductions_ExcludesCompanyGosi()
    {
        // حصة الشركة في الضمان ليست استقطاعاً من الموظف — يجب ألا تدخل استقطاعاته.
        var line = Line();
        Assert.DoesNotContain(line.GosiCompany, new[] { line.TotalDeductions });
        Assert.Equal(line.TaxAmount + line.GosiEmployee + line.OtherDeductions, line.TotalDeductions);
    }

    [Fact]
    public void Earnings_And_Deductions_PartitionByIsAddition()
    {
        var line = Line();
        line.Components.Add(new PayrollRunStore.Component { ItemName = "أساسي", Amount = 400_000m, IsAddition = true });
        line.Components.Add(new PayrollRunStore.Component { ItemName = "بدل نقل", Amount = 50_000m, IsAddition = true });
        line.Components.Add(new PayrollRunStore.Component { ItemName = "ضريبة", Amount = 50_000m, IsAddition = false });

        Assert.Equal(2, line.Earnings.Count());
        Assert.Single(line.Deductions);
        Assert.All(line.Earnings, c => Assert.True(c.IsAddition));
        Assert.All(line.Deductions, c => Assert.False(c.IsAddition));
    }

    [Theory]
    [InlineData("Draft", "مسودة")]
    [InlineData("Calculated", "محتسب")]
    [InlineData("Locked", "مقفل")]
    [InlineData("Issued", "معتمد للصرف")]
    [InlineData("PayslipSent", "أُرسلت القسائم")]
    public void StatusLabel_MapsKnownStatuses(string status, string expected)
    {
        Assert.Equal(expected, PayrollRunStore.StatusLabel(status));
    }

    [Fact]
    public void StatusLabel_UnknownStatus_ReturnsInput()
    {
        Assert.Equal("Mystery", PayrollRunStore.StatusLabel("Mystery"));
    }

    [Fact]
    public void Lifecycle_IsOrderedDraftToPayslipSent()
    {
        Assert.Equal(
            new[] { "Draft", "Calculated", "Locked", "Issued", "PayslipSent" },
            PayrollRunStore.Lifecycle);
    }

    [Fact]
    public void PayrollRun_PeriodText_IsZeroPaddedMonthSlashYear()
    {
        var run = new PayrollRunStore.PayrollRun { Year = 2026, Month = 7 };
        Assert.Equal("07/2026", run.PeriodText);
    }

    [Fact]
    public void PayrollRun_StatusLabelText_MatchesStatusLabel()
    {
        var run = new PayrollRunStore.PayrollRun { Status = "Locked" };
        Assert.Equal("مقفل", run.StatusLabelText);
    }
}
