using System;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات وصل القواعد الفترية بمسار الاقتراحات (الوجبة الأولى، البند 1). الدوال
/// المُختبَرة نقية ولا تمسّ قاعدة البيانات: فضاء معرّفات القواعد ومرساة الفترة — وهما
/// حجرا الأساس اللذان يمنعان تصادم القواعد الفترية بقواعد اليوميات وبحارسَي التعارض،
/// ويجعلان (موظف × فترة × قاعدة) مفتاحاً فريداً فلا تتكرّر الاقتراحات بإعادة التحليل.
/// </summary>
public class PeriodRuleWiringTests
{
    // ===== فضاء معرّفات القواعد =====

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(999)]
    public void RecommendationRuleId_IsNegativeAndOffset(int ruleId)
    {
        var key = PeriodRuleStore.RecommendationRuleId(ruleId);

        Assert.True(key < 0, "معرّف القاعدة الفترية يجب أن يكون سالباً.");
        Assert.Equal(-(PeriodRuleStore.RuleIdOffset + ruleId), key);
    }

    [Fact]
    public void RecommendationRuleId_NeverCollidesWithConflictGuards()
    {
        // حارسا التعارض يستهلكان -1 و-2؛ القاعدة الفترية رقم 1 يجب ألا تصطدم بهما.
        var periodKey = PeriodRuleStore.RecommendationRuleId(1);

        Assert.NotEqual(RecommendationStore.ConflictEarlyLeaveRuleId, periodKey);
        Assert.NotEqual(RecommendationStore.ConflictLateReturnRuleId, periodKey);
    }

    [Fact]
    public void IsPeriodRuleId_SeparatesTheThreeNamespaces()
    {
        // قواعد اليوميات موجبة، حارسا التعارض -1/-2، القواعد الفترية ≤ -1000.
        Assert.False(PeriodRuleStore.IsPeriodRuleId(5));
        Assert.False(PeriodRuleStore.IsPeriodRuleId(RecommendationStore.ConflictEarlyLeaveRuleId));
        Assert.False(PeriodRuleStore.IsPeriodRuleId(RecommendationStore.ConflictLateReturnRuleId));
        Assert.True(PeriodRuleStore.IsPeriodRuleId(PeriodRuleStore.RecommendationRuleId(1)));
        Assert.True(PeriodRuleStore.IsPeriodRuleId(PeriodRuleStore.RecommendationRuleId(250)));
    }

    [Fact]
    public void RecommendationRuleId_IsInjective()
    {
        Assert.NotEqual(
            PeriodRuleStore.RecommendationRuleId(3),
            PeriodRuleStore.RecommendationRuleId(4));
    }

    // ===== مرساة الفترة =====

    [Theory]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 2, 28)]   // سنة غير كبيسة
    [InlineData(2024, 2, 29)]   // سنة كبيسة
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void PeriodAnchorDate_Month_IsLastDayOfMonth(int year, int month, int expectedDay)
    {
        var anchor = PeriodRuleStore.PeriodAnchorDate("Month", year, month);

        Assert.Equal(new DateOnly(year, month, expectedDay), anchor);
    }

    [Fact]
    public void PeriodAnchorDate_Week_IsSundayOfIsoWeek()
    {
        // ISO-8601: الأسبوع 1 من 2026 يبدأ الإثنين 2025-12-29 وينتهي الأحد 2026-01-04.
        var week1 = PeriodRuleStore.PeriodAnchorDate("Week", 2026, 1);

        Assert.Equal(DayOfWeek.Sunday, week1.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 1, 4), week1);
    }

    [Fact]
    public void PeriodAnchorDate_Week_AdvancesSevenDaysPerWeek()
    {
        var week10 = PeriodRuleStore.PeriodAnchorDate("Week", 2026, 10);
        var week11 = PeriodRuleStore.PeriodAnchorDate("Week", 2026, 11);

        Assert.Equal(7, week11.DayNumber - week10.DayNumber);
        Assert.Equal(DayOfWeek.Sunday, week10.DayOfWeek);
    }

    [Fact]
    public void PeriodAnchorDate_DistinctPeriodsNeverShareAnchor()
    {
        // مرساتان متساويتان تعنيان دهس اقتراحات فترة بأخرى بمفتاح منع التكرار.
        var january = PeriodRuleStore.PeriodAnchorDate("Month", 2026, 1);
        var february = PeriodRuleStore.PeriodAnchorDate("Month", 2026, 2);
        var sameMonthNextYear = PeriodRuleStore.PeriodAnchorDate("Month", 2027, 1);

        Assert.NotEqual(january, february);
        Assert.NotEqual(january, sameMonthNextYear);
    }

    // ===== ملخّص المطابقة =====

    [Fact]
    public void Match_Summary_StatesMetricValueAndTier()
    {
        var match = new PeriodRuleStore.Match
        {
            MetricLabel = "إجمالي ساعات التأخير",
            Value = 12.5m,
            RangeText = "10 – 20",
            MetricIsHours = true
        };

        Assert.Equal("إجمالي ساعات التأخير = 12.5 ساعة ضمن الشريحة 10 – 20", match.Summary);
    }

    [Fact]
    public void Match_Summary_UsesDaysUnitForCountMetrics()
    {
        var match = new PeriodRuleStore.Match
        {
            MetricLabel = "أيام الغياب",
            Value = 3m,
            RangeText = "3+",
            MetricIsHours = false
        };

        Assert.Contains("3 يوم", match.Summary);
    }
}
