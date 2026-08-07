using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات فضاء متغيّرات صيغ الرواتب — الحارس الذي يمنع تكرار العطل الذي كشفته
/// دراسة كيان (<c>docs/kayan-payroll-deep-2026-08-08.md</c> القسم 7):
/// المسير كان يمرّر <c>Hours=0, Days=0</c> بينما يمرّر مختبر الصيغ قيماً حقيقية،
/// فتُجرَّب الصيغة بنجاح ثم تُنتج بالقسيمة رقماً آخر — أو تُتخطّى بصمت.
/// </summary>
public class PayrollFormulaVariablesTests
{
    private static PayrollFormulaVariables.Context Sample() => new()
    {
        Basic = 900_000,
        ProratedBasic = 810_000,
        Allowances = 100_000,
        Days = 26,
        PresentDays = 24,
        AbsentDays = 2,
        UnpaidLeaveDays = 1,
        DaysInPeriod = 31,
        Hours = 192,
        DailyRate = 30_000,
        HourlyRate = 3_750,
        Factor = 0.9m,
        IncomeTx = 50_000,
        OvertimeTx = 25_000,
        SalaryDaysTx = -10_000,
        LeaveEncashTx = 15_000,
        DeductionTx = 40_000
    };

    /// <summary>
    /// كل مفتاح معروض بالكتالوج **يجب** أن يوجد بالقاموس المبنيّ. رقاقةٌ تُعرض
    /// للمستخدم ولا يعرفها المُقيِّم = «متغيّر غير معروف» يُسقط الصيغة كلّها.
    /// </summary>
    [Fact]
    public void كل_متغيّر_بالكتالوج_موجود_بالقاموس()
    {
        var vars = PayrollFormulaVariables.Build(Sample());

        foreach (var (key, label, _) in PayrollFormulaVariables.Catalog)
        {
            Assert.True(vars.ContainsKey(key), $"المتغيّر «{label}» ({key}) معروض بالكتالوج ومفقود من القاموس.");
        }
    }

    /// <summary>والعكس: مفتاح يعرفه المسير ولا يراه المستخدم = ميزة خفيّة.</summary>
    [Fact]
    public void كل_مفتاح_بالقاموس_معروض_بالكتالوج()
    {
        var keys = PayrollFormulaVariables.Catalog.Select(v => v.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in PayrollFormulaVariables.Build(Sample()).Keys)
        {
            Assert.True(keys.Contains(key), $"المفتاح «{key}» يُمرَّر للمُقيِّم ولا يظهر بالكتالوج.");
        }
    }

    /// <summary>الكتالوج المعروض بشاشة عناصر الراتب هو الكتالوج نفسه لا نسخة موازية.</summary>
    [Fact]
    public void كتالوج_عناصر_الراتب_مشتقّ_لا_موازٍ()
    {
        Assert.Equal(
            PayrollFormulaVariables.Catalog.Select(v => v.Key),
            SalaryItemStore.FormulaVars.Select(v => v.Key));
    }

    /// <summary>
    /// 🔴 الانحدار الأساسي: <c>Days</c> و<c>Hours</c> يحملان القيم الفعلية لا صفراً.
    /// </summary>
    [Fact]
    public void الأيام_والساعات_ليست_أصفاراً()
    {
        var vars = PayrollFormulaVariables.Build(Sample());

        Assert.Equal(26m, vars["Days"]);
        Assert.Equal(192m, vars["Hours"]);
        Assert.Equal(24m, vars["PresentDays"]);
    }

    /// <summary>صيغة تقسم على الأيام كانت تفشل بقسمة على صفر فيُتخطّى بندها بصمت.</summary>
    [Fact]
    public void القسمة_على_الأيام_تنجح_بعد_الإصلاح()
    {
        var vars = PayrollFormulaVariables.Build(Sample());

        Assert.True(
            SalaryFormulaEvaluator.TryEvaluate("Basic / Days", vars, out var result, out var error),
            error);
        Assert.Equal(900_000m / 26m, result);
    }

    /// <summary>المشتقّات تُحسب بمكان واحد ولا تُمرَّر من الخارج.</summary>
    [Theory]
    [InlineData("Gross", 1_000_000)]        // Basic + Allowances
    [InlineData("NonWorkDays", 5)]          // DaysInPeriod - Days
    [InlineData("TotalIncome", 990_000)]    // 810,000 + 100,000 + 50,000 + 25,000 - 10,000 + 15,000
    [InlineData("TotalDeductions", 40_000)]
    public void المشتقّات_تُحسب_مركزياً(string key, decimal expected)
    {
        Assert.Equal(expected, PayrollFormulaVariables.Build(Sample())[key]);
    }

    /// <summary>
    /// <c>NonWorkDays</c> لا يصير سالباً ولا يُخترع حين لا توجد بيانات حضور
    /// (أيام عمل = صفر ⟶ لا نعلن أن كل الشهر عطلة).
    /// </summary>
    [Fact]
    public void الأيام_غير_التشغيلية_صفر_بلا_بيانات_حضور()
    {
        var vars = PayrollFormulaVariables.Build(new PayrollFormulaVariables.Context
        {
            Basic = 500_000,
            DaysInPeriod = 30,
            Days = 0
        });

        Assert.Equal(0m, vars["NonWorkDays"]);
    }

    /// <summary>القاموس غير حسّاس لحالة الأحرف كما يفترض المُقيِّم.</summary>
    [Fact]
    public void المفاتيح_غير_حسّاسة_لحالة_الأحرف()
    {
        var vars = PayrollFormulaVariables.Build(Sample());

        Assert.True(SalaryFormulaEvaluator.TryEvaluate("basic + ALLOWANCES", vars, out var result, out var error), error);
        Assert.Equal(1_000_000m, result);
    }
}
