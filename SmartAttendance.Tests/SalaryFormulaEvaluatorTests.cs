using System.Collections.Generic;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات مُقيِّم صيغ الرواتب <see cref="SalaryFormulaEvaluator"/> — دالة نقية آمنة،
/// بؤرتها الأسبقية والأقواس والمتغيّرات والدوال (ROUND/MIN/MAX/ABS) وحالات الخطأ.
/// </summary>
public class SalaryFormulaEvaluatorTests
{
    private static readonly Dictionary<string, decimal> Vars = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = 400000m,
        ["Allowances"] = 50000m,
        ["Gross"] = 450000m,
        ["Hours"] = 10m,
        ["Days"] = 3m,
        ["DailyRate"] = 400000m / 30m,
        ["HourlyRate"] = 400000m / 30m / 8m,
    };

    private static decimal Eval(string expr)
    {
        Assert.True(SalaryFormulaEvaluator.TryEvaluate(expr, Vars, out var result, out var error), error);
        return result;
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]           // الأسبقية
    [InlineData("(1 + 2) * 3", 9)]          // الأقواس
    [InlineData("10 - 4 - 3", 3)]           // يسار-الترابط
    [InlineData("2 * -3", -6)]              // سالب أحادي
    [InlineData("-(2 + 3)", -5)]
    [InlineData("100 / 4 / 5", 5)]
    public void Arithmetic(string expr, decimal expected) =>
        Assert.Equal(expected, Eval(expr));

    [Fact]
    public void Variables_CaseInsensitive()
    {
        Assert.Equal(400000m, Eval("Basic"));
        Assert.Equal(400000m, Eval("basic"));
        Assert.Equal(450000m, Eval("Gross"));
    }

    [Fact]
    public void Formula_BasicPercentage() =>
        Assert.Equal(40000m, Eval("Basic * 0.1"));

    [Fact]
    public void Formula_OvertimeStyle() =>
        // الأجر الساعي × 1.5 × الساعات = 600000/240 × 10 = 25000
        Assert.Equal(25000m, System.Math.Round(Eval("HourlyRate * 1.5 * Hours"), 2));

    [Theory]
    [InlineData("ROUND(1234.567)", 1235)]
    [InlineData("ROUND(1234.567, 2)", 1234.57)]
    [InlineData("ROUND(2.5)", 3)]                 // نصف بعيداً عن الصفر
    [InlineData("ABS(-42)", 42)]
    [InlineData("MIN(5, 3, 9)", 3)]
    [InlineData("MAX(5, 3, 9)", 9)]
    [InlineData("MIN(Basic * 0.1, 30000)", 30000)] // سقف بمعادلة
    public void Functions(string expr, decimal expected) =>
        Assert.Equal(expected, Eval(expr));

    [Theory]
    [InlineData("Basic / 0")]                 // قسمة على صفر
    [InlineData("Unknown + 1")]               // متغيّر غير معروف
    [InlineData("FOO(1)")]                     // دالة غير معروفة
    [InlineData("1 + ")]                       // نهاية غير متوقّعة
    [InlineData("1 2")]                        // رمز زائد
    [InlineData("")]                           // فارغة
    public void Errors_ReturnFalse(string expr)
    {
        Assert.False(SalaryFormulaEvaluator.TryEvaluate(expr, Vars, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
