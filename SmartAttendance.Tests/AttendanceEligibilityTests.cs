using System;
using System.Collections.Generic;
using System.Linq;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات الموجة الثانية: **معايير الاستحقاق** على قواعد الحضور، و**توحيد فئات
/// الأثر** بين اليومي والفتري، و**أطول غياب متواصل**.
/// كلّها دوال/خصائص نقية لا تمسّ قاعدة البيانات.
/// </summary>
public class AttendanceEligibilityTests
{
    // ===== معايير الاستحقاق على قاعدة اليوميات =====

    [Fact]
    public void Rule_WithoutConditions_AppliesToEveryone()
    {
        var rule = new ShiftRuleStore.ShiftRule { Name = "تأخير" };

        Assert.True(rule.Conditions.IsEmpty);
        Assert.Equal("كل الموظفين", rule.EligibilityText);
    }

    [Fact]
    public void Rule_ConditionsRoundTripThroughJson()
    {
        var set = new HrConditions.ConditionSet
        {
            Groups =
            {
                new HrConditions.Group
                {
                    Rules = { new HrConditions.Rule { Criterion = "Nationality", Value = "عراقي" } }
                }
            }
        };

        var rule = new ShiftRuleStore.ShiftRule { ConditionsJson = HrConditions.Serialize(set) };

        Assert.False(rule.Conditions.IsEmpty);
        Assert.Equal(1, rule.Conditions.RuleCount);
        Assert.Equal("Nationality", rule.Conditions.Groups.Single().Rules.Single().Criterion);
    }

    [Fact]
    public void Rule_MalformedConditionsJson_DegradesToNoConditions()
    {
        // الغموض لا يجوز أن يمنع القاعدة عن الجميع بصمت — الفارغ يعني «الكل».
        var rule = new ShiftRuleStore.ShiftRule { ConditionsJson = "{ليس JSON}" };

        Assert.True(rule.Conditions.IsEmpty);
    }

    [Fact]
    public void EmptyConditionSet_MatchesWhenEmptyIsTrue_ForAttendanceRules()
    {
        // قواعد الحضور تستعمل matchWhenEmpty:true — قاعدة بلا شروط تنطبق على الجميع
        // (سلوك ما قبل الميزة). لو كانت false لتعطّلت كل القواعد القائمة صامتةً.
        var facts = new Dictionary<string, HrConditions.Fact>();

        Assert.True(HrConditions.Matches(new HrConditions.ConditionSet(), facts, matchWhenEmpty: true));
        Assert.False(HrConditions.Matches(new HrConditions.ConditionSet(), facts, matchWhenEmpty: false));
    }

    // ===== توحيد فئات الأثر =====

    [Fact]
    public void PeriodRuleActionTypes_MatchDailyRuleActionTypes()
    {
        Assert.Equal(ShiftRuleStore.ActionTypes, PeriodRuleStore.ActionTypes);
    }

    [Theory]
    [InlineData("Overtime")]
    [InlineData("Income")]
    [InlineData("Leave")]
    [InlineData("Permission")]
    public void PeriodRules_NowSupportFinancialActions(string actionType)
    {
        // كانت الفترية تدعم مخالفة/ملاحظة/اقتطاع فقط.
        Assert.Contains(PeriodRuleStore.ActionTypes, a => a.Key == actionType);
    }

    // ===== أطول غياب متواصل =====

    private static (DateOnly, bool) Day(int day, bool absent) =>
        (new DateOnly(2026, 3, day), absent);

    [Fact]
    public void LongestAbsenceStreak_CountsConsecutiveRunOnly()
    {
        // غياب 3 متتالية ثم حضور ثم غيابان ⟹ 3
        var days = new[]
        {
            Day(1, true), Day(2, true), Day(3, true),
            Day(4, false),
            Day(5, true), Day(6, true)
        };

        Assert.Equal(3, PeriodRuleStore.LongestAbsenceStreak(days));
    }

    [Fact]
    public void LongestAbsenceStreak_ScatteredAbsencesNeverExceedOne()
    {
        // خمسة أيام غياب متفرّقة — العدد الإجمالي 5 لكن أطول تواصل 1.
        // هذا بالضبط ما يفرّق «الغياب المتقطع» عن «المتواصل» قانونياً.
        var days = new[]
        {
            Day(1, true), Day(2, false), Day(3, true), Day(4, false),
            Day(5, true), Day(6, false), Day(7, true), Day(8, false), Day(9, true)
        };

        Assert.Equal(5, days.Count(d => d.Item2));
        Assert.Equal(1, PeriodRuleStore.LongestAbsenceStreak(days));
    }

    [Fact]
    public void LongestAbsenceStreak_GapInDatesBreaksTheRun()
    {
        // يومان غياب ثم قفزة تاريخ (يوم بلا يومية) ثم غياب — لا تُفترض استمرارية.
        var days = new[] { Day(1, true), Day(2, true), Day(5, true) };

        Assert.Equal(2, PeriodRuleStore.LongestAbsenceStreak(days));
    }

    [Fact]
    public void LongestAbsenceStreak_IsOrderIndependent()
    {
        var shuffled = new[] { Day(3, true), Day(1, true), Day(2, true) };

        Assert.Equal(3, PeriodRuleStore.LongestAbsenceStreak(shuffled));
    }

    [Fact]
    public void LongestAbsenceStreak_NoAbsenceIsZero()
    {
        Assert.Equal(0, PeriodRuleStore.LongestAbsenceStreak(
            new[] { Day(1, false), Day(2, false) }));
        Assert.Equal(0, PeriodRuleStore.LongestAbsenceStreak(Array.Empty<(DateOnly, bool)>()));
    }

    [Fact]
    public void ConsecutiveAbsentDays_IsRegisteredAsDayDerivedMetric()
    {
        // لو لم يُعلَن مشتقّاً لقُرئ من صفّ التجميع (الذي لا يحمله) فأعطى صفراً صامتاً.
        Assert.True(PeriodRuleStore.IsDayDerivedMetric("ConsecutiveAbsentDays"));
        Assert.False(PeriodRuleStore.IsDayDerivedMetric("AbsentDays"));
        Assert.Contains(PeriodRuleStore.Metrics, m => m.Key == "ConsecutiveAbsentDays");
    }
}
