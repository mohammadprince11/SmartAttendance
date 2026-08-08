using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات القيم الثابتة المسمّاة — الجزء النقيّ منها (التحقّق والدمج بفضاء
/// المتغيّرات) بلا قاعدة بيانات.
/// </summary>
public class SalaryConstantTests
{
    private static PayrollFormulaVariables.Context Context() => new()
    {
        Basic = 800_000,
        ProratedBasic = 800_000,
        Allowances = 120_000,
        Days = 30,
        DaysInPeriod = 30,
        DailyRate = 800_000m / 30m,
        Factor = 1m
    };

    /// <summary>
    /// المُقيِّم يقرأ المعرّف بـ<c>char.IsLetter</c>، فمفتاحٌ فيه رقم أو شرطة
    /// **لا تراه أي صيغة أبداً** — يجب أن يُرفض عند الحفظ لا أن يُقبل ويصمت.
    /// </summary>
    [Theory]
    [InlineData("Min Wage")]
    [InlineData("Wage2026")]
    [InlineData("Min-Wage")]
    [InlineData("")]
    [InlineData("   ")]
    public void المفاتيح_غير_الحرفية_تُرفض(string key)
    {
        Assert.NotNull(SalaryConstantStore.Validate(key));
    }

    [Theory]
    [InlineData("MinimumWage")]
    [InlineData("GosiCeiling")]
    public void المفاتيح_الحرفية_تُقبل(string key)
    {
        Assert.Null(SalaryConstantStore.Validate(key));
    }

    /// <summary>ثابتٌ باسم متغيّر مبنيّ يجعل قراءة أي صيغة كذبة ⟶ يُرفض بالحفظ.</summary>
    [Theory]
    [InlineData("Basic")]
    [InlineData("gross")]
    [InlineData("DAYS")]
    public void مصادمة_متغيّر_مبنيّ_تُرفض(string key)
    {
        Assert.NotNull(SalaryConstantStore.Validate(key));
    }

    [Fact]
    public void الثابت_يصير_متاحاً_بالصيغة()
    {
        var vars = PayrollFormulaVariables.Build(
            Context(),
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["MinimumWage"] = 350_000 });

        Assert.True(
            SalaryFormulaEvaluator.TryEvaluate("MAX(Basic, MinimumWage)", vars, out var result, out var error),
            error);
        Assert.Equal(800_000m, result);
    }

    /// <summary>
    /// الأسبقية للمبنيّ: ثابتٌ اسمه <c>Basic</c> (من جدول قديم أو استيراد) لا يحجب
    /// الراتب الأساسي — وإلا انقلب معنى صيغٍ قائمة بلا أن يمسّها أحد.
    /// </summary>
    [Fact]
    public void الثابت_لا_يحجب_متغيّراً_مبنيّاً()
    {
        var vars = PayrollFormulaVariables.Build(
            Context(),
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Basic"] = 1m });

        Assert.Equal(800_000m, vars["Basic"]);
    }

    /// <summary>بلا ثوابت يبقى الفضاء كما هو حرفياً — الدمج إضافة محضة.</summary>
    [Fact]
    public void بلا_ثوابت_الفضاء_لا_يتغيّر()
    {
        Assert.Equal(
            PayrollFormulaVariables.Build(Context()),
            PayrollFormulaVariables.Build(Context(), null));
    }
}
