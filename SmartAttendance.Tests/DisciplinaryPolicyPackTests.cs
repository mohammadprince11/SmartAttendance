using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// لائحة جزاءات الشركة (HRD013) تُستورَد من جدولٍ مكتوب بترميز مختصر — فالترميز
/// يُختبر، لأن خطأً بحرفٍ واحد يعني عقوبةً أشدّ أو أخفّ على موظف حقيقي.
/// </summary>
public class DisciplinaryPolicyPackTests
{
    [Fact]
    public void Dash_and_blank_mean_no_penalty_at_that_step()
    {
        Assert.True(PenaltyStepSyntax.Parse("-").IsEmpty);
        Assert.True(PenaltyStepSyntax.Parse("").IsEmpty);
        Assert.True(PenaltyStepSyntax.Parse(null).IsEmpty);
    }

    [Theory]
    [InlineData("N", PenaltyAction.VerbalWarning)]
    [InlineData("K", PenaltyAction.WrittenWarning)]
    [InlineData("A1", PenaltyAction.Warning)]
    [InlineData("A2", PenaltyAction.FinalWarning)]
    [InlineData("F", PenaltyAction.Termination)]
    public void Warning_codes_map_to_their_action_type(string code, string expected) =>
        Assert.Equal(expected, PenaltyStepSyntax.Parse(code).ActionType);

    [Fact]
    public void Warning_codes_carry_no_money()
    {
        var step = PenaltyStepSyntax.Parse("A2");

        Assert.Equal("None", step.ImpactType);
        Assert.Equal(0m, step.Value);
    }

    [Theory]
    [InlineData("D0.25", 0.25, "خصم أجر ¼ يوم")]
    [InlineData("D0.5", 0.5, "خصم أجر ½ يوم")]
    [InlineData("D1", 1, "خصم أجر يوم")]
    [InlineData("D2", 2, "خصم أجر يومين")]
    [InlineData("D5", 5, "خصم أجر 5 أيام")]
    public void Deduction_codes_carry_days_and_read_as_the_policy_writes_them(
        string code, decimal days, string title)
    {
        var step = PenaltyStepSyntax.Parse(code);

        Assert.Equal(PenaltyAction.SalaryDeduction, step.ActionType);
        Assert.Equal("Days", step.ImpactType);
        Assert.Equal(days, step.Value);
        Assert.Equal(title, step.Title);
    }

    [Fact]
    public void A_combined_step_keeps_the_money_as_its_type_and_both_words_in_its_title()
    {
        // «4 أيام + إنذار أول»: النوع هو الخصم لأنه ما يُحتسب، والإنذار يبقى بالعنوان
        // فيظهر بالكتاب الرسمي. صفّان لدرجةٍ واحدة كانا سيضاعفان العقوبة.
        var step = PenaltyStepSyntax.Parse("D4+A1");

        Assert.Equal(PenaltyAction.SalaryDeduction, step.ActionType);
        Assert.Equal(4m, step.Value);
        Assert.Equal("خصم أجر 4 أيام + إنذار أول", step.Title);
    }

    [Fact]
    public void Among_non_financial_codes_the_severest_wins_the_type()
    {
        var step = PenaltyStepSyntax.Parse("K+A2");

        Assert.Equal(PenaltyAction.FinalWarning, step.ActionType);
    }

    [Fact]
    public void Unknown_codes_are_ignored_rather_than_becoming_a_deduction() =>
        Assert.True(PenaltyStepSyntax.Parse("ZZ").IsEmpty);

    // ── الجدول نفسه ──────────────────────────────────────────────────────────

    [Fact]
    public void Every_violation_belongs_to_a_declared_category_and_has_at_least_one_step()
    {
        foreach (var (category, name, steps) in DisciplinaryPolicyPack.Violations)
        {
            Assert.InRange(category, 0, DisciplinaryPolicyPack.Categories.Length - 1);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.NotEmpty(steps);
            Assert.Contains(steps, s => !PenaltyStepSyntax.Parse(s).IsEmpty);
        }
    }

    [Fact]
    public void No_violation_is_listed_twice()
    {
        var names = DisciplinaryPolicyPack.Violations.Select(v => v.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Escalation_never_gets_lighter_as_the_offence_repeats()
    {
        // سلّمٌ ينزل بدل أن يصعد يعني خطأ نقلٍ عن الورقة.
        //
        // ⚠️ **«الإنذار بالفصل» أشدّ من الخصم لا أخفّ منه.** كانت هذه الدالة ترتّبه
        // دون الخصم فسقط الاختبار على «التمارض» (خصم 5 أيام ⟵ إنذار نهائي)، والخطأ
        // كان بالترتيب لا باللائحة: جداول الشركة كلّها تضع الإنذار بالفصل **بعد**
        // الخصومات — لأنه آخر درجةٍ قبل إنهاء الخدمة، لا عقوبةً ماليةً أخفّ.
        int Rank(string code)
        {
            var step = PenaltyStepSyntax.Parse(code);
            return step.ActionType switch
            {
                PenaltyAction.Termination => 1000,
                PenaltyAction.FinalWarning => 500,
                PenaltyAction.Warning => 300,
                PenaltyAction.SalaryDeduction => 100 + (int)(step.Value * 4),
                PenaltyAction.WrittenWarning => 20,
                PenaltyAction.VerbalWarning => 10,
                _ => 0
            };
        }

        foreach (var (_, name, steps) in DisciplinaryPolicyPack.Violations)
        {
            var ranks = steps.Where(s => !PenaltyStepSyntax.Parse(s).IsEmpty).Select(Rank).ToList();

            for (var i = 1; i < ranks.Count; i++)
            {
                Assert.True(ranks[i] >= ranks[i - 1],
                    $"«{name}»: الدرجة {i + 1} أخفّ من التي قبلها.");
            }
        }
    }

    [Fact]
    public void Termination_never_drops_but_warnings_and_deductions_do()
    {
        Assert.Equal(0, DisciplinaryPolicyPack.DropMonths(PenaltyAction.Termination, 0));
        Assert.Equal(1, DisciplinaryPolicyPack.DropMonths(PenaltyAction.VerbalWarning, 0));
        Assert.Equal(12, DisciplinaryPolicyPack.DropMonths(PenaltyAction.FinalWarning, 0));
    }

    [Fact]
    public void Deduction_validity_is_three_months_in_category_A_and_six_elsewhere()
    {
        // نصّ اللائحة صراحةً: 3 أشهر لفئة «أ»، و6 لفئتَي «ب» و«ج».
        Assert.Equal(3, DisciplinaryPolicyPack.DropMonths(PenaltyAction.SalaryDeduction, 0));
        Assert.Equal(6, DisciplinaryPolicyPack.DropMonths(PenaltyAction.SalaryDeduction, 1));
        Assert.Equal(6, DisciplinaryPolicyPack.DropMonths(PenaltyAction.SalaryDeduction, 2));
    }

    // ── سقف الاقتطاع (مادة 6 و9) ─────────────────────────────────────────────

    [Fact]
    public void Monthly_deduction_is_capped_at_the_policy_percentage()
    {
        // راتب مليون · خصومات 400 ألف · السقف 20% ⟹ 200 ألف.
        Assert.Equal(200_000m, PenaltyBasePool.CapMonthlyDeduction(400_000m, 1_000_000m, 20m));
    }

    [Fact]
    public void A_total_below_the_cap_passes_through_untouched() =>
        Assert.Equal(150_000m, PenaltyBasePool.CapMonthlyDeduction(150_000m, 1_000_000m, 20m));

    [Fact]
    public void Zero_percent_means_no_cap_not_a_zero_deduction()
    {
        // «بلا سقف» و«اخصم صفراً» نقيضان — والخلط بينهما يُلغي كل خصومات النظام.
        Assert.Equal(400_000m, PenaltyBasePool.CapMonthlyDeduction(400_000m, 1_000_000m, 0m));
        Assert.Equal(400_000m, PenaltyBasePool.CapMonthlyDeduction(400_000m, 1_000_000m, -5m));
    }

    [Fact]
    public void An_unknown_salary_leaves_the_deduction_alone() =>
        Assert.Equal(400_000m, PenaltyBasePool.CapMonthlyDeduction(400_000m, 0m, 20m));

    [Fact]
    public void The_cap_applies_to_the_total_so_repetition_cannot_evade_it()
    {
        // خمس مخالفات بخمسين ألفاً لكلٍّ = 250 ألفاً؛ السقف يردّها إلى 200 ألف.
        var total = Enumerable.Repeat(50_000m, 5).Sum();

        Assert.Equal(200_000m, PenaltyBasePool.CapMonthlyDeduction(total, 1_000_000m, 20m));
    }
}
