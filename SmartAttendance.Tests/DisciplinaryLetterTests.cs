using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// كتاب العقوبة وثيقةٌ تُبلَّغ للموظف ويُبنى عليها حقّ الاعتراض — فتركيبها يُختبر
/// لا يُجرَّب حياً على مخالفةٍ حقيقية.
/// </summary>
public class DisciplinaryLetterTests
{
    private static DisciplinaryLetter.Context Sample(string articleNo = "3/ب") => new(
        ReferenceNo: "VC26-007",
        EmployeeName: "أحمد الجبوري",
        EmployeeCode: "2738",
        Department: "الإنتاج",
        Position: "فنّي",
        EventDate: new DateOnly(2026, 7, 14),
        ViolationCategory: "مخالفات الحضور والانصراف",
        ViolationName: "التأخر عن موعد الدوام الرسمي",
        Description: "تأخّر 40 دقيقة",
        PenaltyAction: "إنذار خطّي",
        FinancialImpact: "لا أثر مالي",
        DeductionAmount: 0,
        ArticleNo: articleNo,
        ApprovedBy: "لجنة الانضباط",
        OccurrenceNumber: 2);

    [Fact]
    public void Render_replaces_every_documented_token()
    {
        var template = string.Join(" | ", DisciplinaryLetter.Tokens);

        var rendered = DisciplinaryLetter.Render(template, Sample(), new DateOnly(2026, 8, 2));

        // أي رمز موثَّق ولا يُستبدل يظهر بالكتاب حرفياً أمام الموظف.
        Assert.Empty(DisciplinaryLetter.UnresolvedTokens(rendered));
    }

    [Fact]
    public void Render_fills_reference_penalty_and_dates()
    {
        var rendered = DisciplinaryLetter.Render(
            "{ReferenceNo} · {ViolationDate} · {PenaltyAction} · {IssueDate} · {OccurrenceNumber}",
            Sample(),
            new DateOnly(2026, 8, 2));

        Assert.Equal("VC26-007 · 2026-07-14 · إنذار خطّي · 2026-08-02 · 2", rendered);
    }

    [Fact]
    public void Missing_article_renders_an_explicit_dash_not_an_empty_gap()
    {
        // فراغٌ مكان الفقرة يُقرأ سهواً مطبعياً؛ «—» يُقرأ نقصاً.
        var rendered = DisciplinaryLetter.Render("الفقرة {ArticleNo}", Sample(articleNo: "  "), new DateOnly(2026, 8, 2));

        Assert.Equal("الفقرة —", rendered);
    }

    [Fact]
    public void Unknown_tokens_survive_rendering_and_are_reported()
    {
        var rendered = DisciplinaryLetter.Render("{EmployeeName} — {Foo} — {Bar}", Sample(), new DateOnly(2026, 8, 2));

        Assert.Contains("{Foo}", rendered);
        Assert.Equal(new[] { "{Foo}", "{Bar}" }, DisciplinaryLetter.UnresolvedTokens(rendered));
    }

    [Fact]
    public void Fallback_body_is_self_sufficient()
    {
        // النصّ الاحتياطي يخرج حين لا قالب — فلا يصحّ أن يترك رمزاً معلّقاً.
        var rendered = DisciplinaryLetter.Render(DisciplinaryLetter.FallbackBody, Sample(), new DateOnly(2026, 8, 2));

        Assert.Empty(DisciplinaryLetter.UnresolvedTokens(rendered));
        Assert.Contains("VC26-007", rendered);
        Assert.Contains("3/ب", rendered);
    }

    [Theory]
    [InlineData("Days", 1.5, "خصم 1.5 يوم من الراتب")]
    [InlineData("Amount", 25000, "خصم مبلغ 25,000")]
    [InlineData("Percent", 50, "خصم 50% من الراتب")]
    [InlineData("None", 0, "لا أثر مالي")]
    // قيمةٌ صفرية بنوعٍ ماليّ ليست «خصم 0»: لا أثر، وهذا ما يُكتب.
    [InlineData("Days", 0, "لا أثر مالي")]
    public void Financial_impact_text_reads_as_a_sentence(string type, decimal value, string expected) =>
        Assert.Equal(expected, DisciplinaryLetter.FinancialImpactText(type, value));

    [Fact]
    public void Empty_template_renders_nothing_rather_than_throwing() =>
        Assert.Equal(string.Empty, DisciplinaryLetter.Render(null, Sample(), new DateOnly(2026, 8, 2)));
}
