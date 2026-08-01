using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات إضافة المقارنة وIF لمحرّك الصيغ.
///
/// **الاختبار الأهمّ هو <see cref="ExistingStyleFormulas_EvaluateIdentically"/>**:
/// المحرّك يقيّم صيغ رواتب حيّة، وأي انحراف بناتج صيغة قائمة يغيّر قسائم بأثر رجعي.
/// فالإضافة يجب أن تكون **محضة**: صيغةٌ بلا رمز مقارنة تمرّ بنفس المسار وبنفس الناتج.
/// </summary>
public class SalaryFormulaConditionalTests
{
    private static readonly Dictionary<string, decimal> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = 1_000_000m,
        ["Allowances"] = 250_000m,
        ["Gross"] = 1_250_000m,
        ["Hours"] = 8m,
        ["Days"] = 30m,
        ["DailyRate"] = 33_333.33m,
        ["HourlyRate"] = 4_166.67m
    };

    private static decimal Eval(string expression)
    {
        Assert.True(
            SalaryFormulaEvaluator.TryEvaluate(expression, Vars, out var result, out var error),
            $"فشل التقييم: {error}");
        return result;
    }

    private static string? Error(string expression)
    {
        SalaryFormulaEvaluator.TryEvaluate(expression, Vars, out _, out var error);
        return error;
    }

    [Fact]
    public void ExistingStyleFormulas_EvaluateIdentically()
    {
        // صيغ بنمط ما هو مستعمل اليوم — يجب ألا يتغيّر ناتجها بمقدار فلس.
        Assert.Equal(100_000m, Eval("Basic * 0.1"));
        Assert.Equal(250_000m, Eval("MIN(Basic * 0.25, 300000)"));
        Assert.Equal(1_250_000m, Eval("Basic + Allowances"));
        Assert.Equal(33_333.33m, Eval("ROUND(Basic / 30, 2)"));
        Assert.Equal(300_000m, Eval("MIN(Basic * 0.5, 300000)"));
        Assert.Equal(-50_000m, Eval("-(Basic * 0.05)"));
        Assert.Equal(1_000_000m, Eval("MAX(Basic, Allowances)"));
    }

    // ── المقارنة ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Basic > 500000", 1)]
    [InlineData("Basic < 500000", 0)]
    [InlineData("Basic >= 1000000", 1)]
    [InlineData("Basic <= 999999", 0)]
    [InlineData("Basic = 1000000", 1)]
    [InlineData("Basic <> 1000000", 0)]
    public void ComparisonReturnsOneOrZero(string expression, int expected)
    {
        Assert.Equal(expected, Eval(expression));
    }

    [Fact]
    public void ComparisonCanBeMultipliedDirectly()
    {
        // نمط مفيد: قيمة تُصفَّر بشرط بلا IF.
        Assert.Equal(1_000_000m, Eval("Basic * (Basic > 500000)"));
        Assert.Equal(0m, Eval("Basic * (Basic > 5000000)"));
    }

    [Fact]
    public void ComparisonBindsLooserThanArithmetic()
    {
        // 1000000 - 250000 = 750000 > 500000 ⟹ 1
        Assert.Equal(1m, Eval("Basic - Allowances > 500000"));
    }

    // ── IF ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IfPicksBranchByCondition()
    {
        Assert.Equal(50_000m, Eval("IF(Basic > 500000, Basic * 0.05, 0)"));
        Assert.Equal(0m, Eval("IF(Basic > 5000000, Basic * 0.05, 0)"));
    }

    [Fact]
    public void IfTreatsAnyNonZeroAsTrue()
    {
        Assert.Equal(10m, Eval("IF(1, 10, 20)"));
        Assert.Equal(20m, Eval("IF(0, 10, 20)"));
        Assert.Equal(10m, Eval("IF(-5, 10, 20)"));
    }

    [Fact]
    public void NestedIf_SupportsTaxBrackets()
    {
        // شريحة ثلاثية — وهي سبب إضافة IF أصلاً.
        const string brackets = "IF(Gross <= 500000, 0, IF(Gross <= 1000000, (Gross - 500000) * 0.03, (Gross - 1000000) * 0.05 + 15000))";

        Assert.Equal(27_500m, Eval(brackets));
    }

    [Fact]
    public void IfRequiresExactlyThreeArguments()
    {
        Assert.Contains("ثلاثة وسائط", Error("IF(1, 2)"));
        Assert.Contains("ثلاثة وسائط", Error("IF(1, 2, 3, 4)"));
    }

    [Fact]
    public void IfEvaluatesAllBranchesEagerly_SoUnsafeDivisionStillFails()
    {
        // قيدٌ موثَّق لا عطل: الفرع غير المأخوذ يُقيَّم أيضاً.
        var error = Error("IF(1, 10, Basic / 0)");

        Assert.Equal("قسمة على صفر.", error);
    }

    [Fact]
    public void SafeDivisionPattern_Works()
    {
        // الحلّ الموصى به بالواجهة.
        Assert.Equal(33_333.333333333333333333333333m, Eval("IF(Days > 0, Basic / MAX(Days, 1), 0)"));
    }

    // ── حراسة الأخطاء ──────────────────────────────────────────────────────────

    [Fact]
    public void UnknownFunctionStillRejected()
    {
        Assert.Contains("دالة غير معروفة", Error("SUM(1, 2)"));
    }

    [Fact]
    public void DanglingComparisonOperator_IsAnError()
    {
        Assert.NotNull(Error("Basic >"));
    }
}
