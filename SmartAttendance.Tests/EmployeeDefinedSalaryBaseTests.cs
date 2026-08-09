using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات وعاء الضريبة/الضمان المُعرَّف بالموظف (السيناريوهان الرابع والخامس من
/// الموظف الذهبي): راتب ضمان 400,000، راتب ضريبة 400,000.
///
/// يجب أن يفشل السلوك ضدّ القديم (الذي كان يحسب الضمان على الإجمالي 2,000,000)
/// وينجح بعد التصحيح — الوعاء يصير 400,000 من الرقم المُدخَل.
/// </summary>
public class EmployeeDefinedSalaryBaseTests
{
    [Fact]
    public void DefaultMode_UsesComposedBase_BackwardCompatible()
    {
        // النمط الفارغ = «مكوّنات» ⟹ الوعاء المُركَّب (سلوك اليوم حرفياً).
        var result = EmployeeDefinedSalaryBase.Resolve(null, employeeValue: 400_000m, composed: 2_000_000m);

        Assert.Equal(2_000_000m, result.Base);
        Assert.Equal(EmployeeDefinedSalaryBase.Source.Composed, result.Source);
    }

    [Fact]
    public void GosiEmployeeDefined_UsesSocialSecuritySalary_NotGross()
    {
        // السيناريو الرابع: راتب الضمان 400,000 يصير الوعاء بدل الإجمالي 2,000,000.
        var result = EmployeeDefinedSalaryBase.Resolve(
            EmployeeDefinedSalaryBase.ModeEmployeeDefined, employeeValue: 400_000m, composed: 2_000_000m);

        Assert.Equal(400_000m, result.Base);
        Assert.Equal(EmployeeDefinedSalaryBase.Source.Employee, result.Source);
    }

    [Fact]
    public void GosiEmployeeDefined_ExactShares_5And12Percent()
    {
        // الوعاء 400,000 × 5% = 20,000 (موظف) · × 12% = 48,000 (شركة).
        var gosiBase = EmployeeDefinedSalaryBase.Resolve(
            EmployeeDefinedSalaryBase.ModeEmployeeDefined, 400_000m, 2_000_000m).Base;

        var profile = new PayrollConfigStore.GosiProfile { EmployeeRate = 5m, CompanyRate = 12m, Ceiling = 0m };
        var (employee, company) = PayrollConfigStore.ComputeGosi(gosiBase, profile);

        Assert.Equal(20_000m, employee);
        Assert.Equal(48_000m, company);
    }

    [Fact]
    public void TaxEmployeeDefined_TaxComputedFrom400k_Not2M()
    {
        // السيناريو الخامس: وعاء الضريبة 400,000 من الرقم المُدخَل.
        var taxBase = EmployeeDefinedSalaryBase.Resolve(
            EmployeeDefinedSalaryBase.ModeEmployeeDefined, 400_000m, 2_000_000m).Base;

        Assert.Equal(400_000m, taxBase);

        // ملف ضريبة اختباريّ حتميّ (لا يعتمد بذور الإنتاج): إعفاء 250,000 ثم شرائح.
        var profile = new PayrollConfigStore.TaxProfile
        {
            ExemptionAmount = 250_000m,
            Brackets =
            {
                new PayrollConfigStore.TaxBracket { FromAmount = 0m,       ToAmount = 250_000m, Rate = 3m },
                new PayrollConfigStore.TaxBracket { FromAmount = 250_000m, ToAmount = null,     Rate = 5m }
            }
        };

        // خاضع بعد الإعفاء = 400,000 − 250,000 = 150,000، كلّه بالشريحة الأولى 3% = 4,500.
        var taxFrom400k = PayrollConfigStore.ComputeTax(taxBase, profile);
        Assert.Equal(4_500m, taxFrom400k);

        // إثباتٌ أنه ليس على 2,000,000: الضريبة عندها أكبر بكثير.
        var taxFrom2M = PayrollConfigStore.ComputeTax(2_000_000m, profile);
        Assert.True(taxFrom2M > taxFrom400k);
        Assert.NotEqual(taxFrom400k, taxFrom2M);
    }

    [Fact]
    public void EmployeeDefinedButMissingValue_FlaggedNotSilent()
    {
        // «مُعرَّف بالموظف» بلا رقم: رجوعٌ معلَّم (EmployeeMissing) لا صامت.
        var zero = EmployeeDefinedSalaryBase.Resolve(EmployeeDefinedSalaryBase.ModeEmployeeDefined, 0m, 2_000_000m);
        var nul = EmployeeDefinedSalaryBase.Resolve(EmployeeDefinedSalaryBase.ModeEmployeeDefined, null, 2_000_000m);

        Assert.Equal(EmployeeDefinedSalaryBase.Source.EmployeeMissing, zero.Source);
        Assert.Equal(EmployeeDefinedSalaryBase.Source.EmployeeMissing, nul.Source);
    }
}
