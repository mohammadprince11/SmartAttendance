using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات انحدار لتركيب أوعية الضريبة والضمان.
///
/// الغرض الأول منها **تثبيت السلوك القائم**: قبل هذا الفصل كان وعاء الضريبة
/// مجموعَ (الأساسي + العلاوات + الخاضع للضريبة بأنواعه) ووعاء الضمان هو
/// الإجمالي كاملاً. أي تغيير مستقبلي يجعل العضوية قابلة للتحرير يجب أن يبقي
/// هذين الافتراضيين كما هما بالضبط، وإلا تغيّر اقتطاع كل قسيمة بأثر رجعي.
/// </summary>
public class SalaryBaseComposerTests
{
    private static SalaryBaseComposer.Amounts Sample() => new()
    {
        Basic = 500_000m,
        Allowances = 120_000m,
        TaxableIncome = 40_000m,
        TaxableOvertime = 25_000m,
        SalaryDays = 10_000m,
        LeaveEncashment = 15_000m,
        FormulaAdd = 5_000m,
        Gross = 715_000m
    };

    [Fact]
    public void TaxBase_MatchesPreExtractionFormula()
    {
        var a = Sample();

        // الصيغة التي كانت مثبّتة بالمحرك حرفياً
        var expected = a.Basic + a.Allowances + a.TaxableIncome + a.TaxableOvertime
                       + a.SalaryDays + a.LeaveEncashment + a.FormulaAdd;

        Assert.Equal(715_000m, expected);
        Assert.Equal(expected, SalaryBaseComposer.TaxBase(a));
    }

    [Fact]
    public void GosiBase_IsGrossOnly_NotTheTaxBase()
    {
        var a = Sample();
        a.Gross = 900_000m;   // الإجمالي قد يخالف مجموع مكوّنات وعاء الضريبة

        Assert.Equal(900_000m, SalaryBaseComposer.GosiBase(a));
    }

    [Fact]
    public void Compose_EmptyMembership_IsZero()
    {
        Assert.Equal(0m, SalaryBaseComposer.Compose(Sample(), new string[0]));
    }

    [Fact]
    public void Compose_NullMembership_IsZero()
    {
        Assert.Equal(0m, SalaryBaseComposer.Compose(Sample(), null));
    }

    [Fact]
    public void Compose_UnknownMemberIsIgnored_RatherThanThrowing()
    {
        // عضوية محفوظة تشير لمكوّن أُزيل — يجب ألا تُسقط المسير
        var value = SalaryBaseComposer.Compose(Sample(), new[] { "Basic", "NoSuchComponent" });

        Assert.Equal(500_000m, value);
    }

    [Fact]
    public void Compose_CustomMembership_SumsOnlyChosenComponents()
    {
        // «أي مبلغ ممكن يدخل ضمان أو ضريبة»: وعاء ضمان = أساسي + علاوات فقط
        var value = SalaryBaseComposer.Compose(Sample(), new[] { "Basic", "Allowances" });

        Assert.Equal(620_000m, value);
    }

    [Fact]
    public void Compose_TrimsWhitespaceInMemberKeys()
    {
        Assert.Equal(500_000m, SalaryBaseComposer.Compose(Sample(), new[] { " Basic " }));
    }

    [Fact]
    public void Compose_DuplicateMemberCountsTwice_SoCallersMustDeduplicate()
    {
        // توثيق مقصود: التركيب جمعٌ خام، ومنعُ التكرار مسؤولية طبقة الحفظ.
        Assert.Equal(1_000_000m, SalaryBaseComposer.Compose(Sample(), new[] { "Basic", "Basic" }));
    }
}
