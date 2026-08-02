using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// موادّ اللائحة الانضباطية صارت منطقاً يمنع ويُنبّه — فتُختبر كما تُختبر أي
/// قاعدةٍ تمسّ حقّ موظف.
/// </summary>
public class DisciplinaryPolicyRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 2);

    // ── مادة 6 · تقادم الاتهام ───────────────────────────────────────────────

    [Fact]
    public void An_accusation_older_than_the_limit_is_barred()
    {
        Assert.True(DisciplinaryPolicyRules.IsStale(new DateOnly(2026, 6, 1), Today, 30));
        Assert.False(DisciplinaryPolicyRules.IsStale(new DateOnly(2026, 7, 20), Today, 30));
    }

    [Fact]
    public void The_limit_is_exclusive_on_its_last_day()
    {
        // اليوم الثلاثون ما زال جائزاً؛ الحادي والثلاثون لا.
        Assert.False(DisciplinaryPolicyRules.IsStale(Today.AddDays(-30), Today, 30));
        Assert.True(DisciplinaryPolicyRules.IsStale(Today.AddDays(-31), Today, 30));
    }

    [Fact]
    public void Criminal_offences_are_exempt_from_the_time_bar() =>
        // نصّ المادة: «باستثناء المخالفات التي تشكّل جرائم جنائية».
        Assert.False(DisciplinaryPolicyRules.IsStale(new DateOnly(2024, 1, 1), Today, 30, isCriminal: true));

    [Fact]
    public void A_zero_limit_means_no_time_bar_not_everything_barred() =>
        // الخلط بينهما كان سيمنع كل تسجيل بالنظام.
        Assert.False(DisciplinaryPolicyRules.IsStale(new DateOnly(2020, 1, 1), Today, 0));

    // ── مادة 5 · نافذة التكرار ───────────────────────────────────────────────

    [Fact]
    public void A_violation_with_no_recent_precedent_is_the_first_one()
    {
        var previous = new[] { new DateOnly(2024, 1, 1) };

        Assert.Equal(1, DisciplinaryPolicyRules.OccurrenceNumber(previous, Today));
    }

    [Fact]
    public void Precedents_inside_the_year_make_it_a_repeat()
    {
        var previous = new[]
        {
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 5, 1),
            new DateOnly(2023, 1, 1)   // خارج النافذة — لا تُحتسب
        };

        Assert.Equal(3, DisciplinaryPolicyRules.OccurrenceNumber(previous, Today));
    }

    [Fact]
    public void A_later_violation_does_not_turn_an_earlier_one_into_a_repeat()
    {
        // المخالفة المسجَّلة بأثرٍ رجعيّ يجب أن تُقاس بما سبقها هي، لا بما تلاها.
        var previous = new[] { new DateOnly(2026, 7, 1) };

        Assert.Equal(1, DisciplinaryPolicyRules.OccurrenceNumber(previous, new DateOnly(2026, 3, 1)));
    }

    // ── «هام» · التصعيد التعاقدي ─────────────────────────────────────────────

    [Theory]
    [InlineData("ب — مخالفات نظام العمل", true)]
    [InlineData("ج — مخالفات سلوكية قد تؤدي إلى الفصل", true)]
    [InlineData("أ — مخالفات الحضور والانصراف والمظهر العام", false)]
    [InlineData("ت — مخالفات المنطقة الأمامية", false)]
    [InlineData(null, false)]
    public void Only_categories_B_and_C_count_toward_escalation(string? category, bool expected) =>
        Assert.Equal(expected, DisciplinaryPolicyRules.CountsForEscalation(category));

    [Fact]
    public void Five_penalties_reach_the_non_renewal_threshold()
    {
        Assert.False(DisciplinaryPolicyRules.ReachedNonRenewal(4));
        Assert.True(DisciplinaryPolicyRules.ReachedNonRenewal(5));
        Assert.True(DisciplinaryPolicyRules.ReachedNonRenewal(9));
    }

    [Fact]
    public void A_zero_threshold_disables_the_non_renewal_notice() =>
        Assert.False(DisciplinaryPolicyRules.ReachedNonRenewal(99, threshold: 0));

    [Fact]
    public void Immediate_termination_needs_both_an_active_warning_and_a_B_or_C_offence()
    {
        Assert.True(DisciplinaryPolicyRules.TriggersImmediateTermination(true, "ب — نظام العمل"));

        // إنذارٌ ساقط لا يُفعّل القاعدة.
        Assert.False(DisciplinaryPolicyRules.TriggersImmediateTermination(false, "ب — نظام العمل"));

        // ومخالفة حضورٍ لا تُفعّلها ولو كان الإنذار سارياً.
        Assert.False(DisciplinaryPolicyRules.TriggersImmediateTermination(true, "أ — الحضور"));
    }

    // ── مادة 6 · التحقيق قبل الجزاء ──────────────────────────────────────────

    [Fact]
    public void Investigation_is_required_only_for_serious_penalties_and_only_when_enabled()
    {
        Assert.True(DisciplinaryPolicyRules.RequiresInvestigation(PenaltyAction.SalaryDeduction, enforce: true));
        Assert.True(DisciplinaryPolicyRules.RequiresInvestigation(PenaltyAction.Termination, enforce: true));

        // «يجوز الاكتفاء بالتحقيق الشفهي بالمخالفات البسيطة» — فلا يُوقَف تنبيه.
        Assert.False(DisciplinaryPolicyRules.RequiresInvestigation(PenaltyAction.VerbalWarning, enforce: true));

        // ومعطَّلةً لا تُشترط لشيء.
        Assert.False(DisciplinaryPolicyRules.RequiresInvestigation(PenaltyAction.Termination, enforce: false));
    }

}
