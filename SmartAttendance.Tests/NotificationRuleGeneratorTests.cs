using SmartAttendance.Web.Infrastructure.Notifications;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية لمنطق المطابقة الزمنية بمولّد مركز الإشعارات — بلا قاعدة بيانات:
/// الذكرى السنوية (ميلاد/عمل)، نافذة الاستحقاق (عقد/تجربة)، وحساب نهاية فترة التجربة.
/// </summary>
public class NotificationRuleGeneratorTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ─────────────── الذكرى السنوية ───────────────

    [Fact]
    public void IsAnnualMatchToday_MatchesSameMonthDay_AnyYear()
    {
        Assert.True(NotificationRuleGenerator.IsAnnualMatchToday(D(1990, 7, 26), D(2026, 7, 26)));
    }

    [Fact]
    public void IsAnnualMatchToday_False_WhenDifferentDay()
    {
        Assert.False(NotificationRuleGenerator.IsAnnualMatchToday(D(1990, 7, 26), D(2026, 7, 25)));
    }

    [Fact]
    public void IsAnnualMatchToday_False_WhenNull()
    {
        Assert.False(NotificationRuleGenerator.IsAnnualMatchToday(null, D(2026, 7, 26)));
    }

    [Fact]
    public void IsAnnualMatchToday_Feb29_FiresOnFeb28_InNonLeapYear()
    {
        Assert.True(NotificationRuleGenerator.IsAnnualMatchToday(D(2000, 2, 29), D(2027, 2, 28))); // 2027 غير كبيسة
        Assert.False(NotificationRuleGenerator.IsAnnualMatchToday(D(2000, 2, 29), D(2028, 2, 28))); // 2028 كبيسة ⟹ ينتظر 29
        Assert.True(NotificationRuleGenerator.IsAnnualMatchToday(D(2000, 2, 29), D(2028, 2, 29)));
    }

    // ─────────────── سنوات ذكرى العمل ───────────────

    [Fact]
    public void AnniversaryYears_ReturnsElapsedYears_OnMatch()
    {
        Assert.Equal(6, NotificationRuleGenerator.AnniversaryYears(D(2020, 7, 26), D(2026, 7, 26)));
    }

    [Fact]
    public void AnniversaryYears_Zero_OnHireDayItself()
    {
        Assert.Equal(0, NotificationRuleGenerator.AnniversaryYears(D(2026, 7, 26), D(2026, 7, 26)));
    }

    [Fact]
    public void AnniversaryYears_Zero_WhenNotMatchingDay()
    {
        Assert.Equal(0, NotificationRuleGenerator.AnniversaryYears(D(2020, 7, 26), D(2026, 7, 27)));
    }

    // ─────────────── نافذة الاستحقاق ───────────────

    [Fact]
    public void WithinWindow_True_WhenTargetToday()
    {
        Assert.True(NotificationRuleGenerator.WithinWindow(D(2026, 7, 26), D(2026, 7, 26), 45));
    }

    [Fact]
    public void WithinWindow_True_WhenInsideWindow()
    {
        Assert.True(NotificationRuleGenerator.WithinWindow(D(2026, 8, 20), D(2026, 7, 26), 45)); // 25 يوماً
    }

    [Fact]
    public void WithinWindow_False_WhenBeyondWindow()
    {
        Assert.False(NotificationRuleGenerator.WithinWindow(D(2026, 10, 1), D(2026, 7, 26), 45));
    }

    [Fact]
    public void WithinWindow_False_WhenPast()
    {
        Assert.False(NotificationRuleGenerator.WithinWindow(D(2026, 7, 25), D(2026, 7, 26), 45));
    }

    [Fact]
    public void WithinWindow_False_WhenNull()
    {
        Assert.False(NotificationRuleGenerator.WithinWindow(null, D(2026, 7, 26), 45));
    }

    // ─────────────── نهاية فترة التجربة ───────────────

    [Fact]
    public void ProbationEnd_Days_AddsDuration()
    {
        Assert.Equal(D(2026, 4, 1),
            NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Day", 90, false, 0));
    }

    [Fact]
    public void ProbationEnd_Month_AddsMonths()
    {
        Assert.Equal(D(2026, 4, 1),
            NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Month", 3, false, 0));
    }

    [Fact]
    public void ProbationEnd_Week_AddsWeeks()
    {
        Assert.Equal(D(2026, 1, 15),
            NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Week", 2, false, 0));
    }

    [Fact]
    public void ProbationEnd_AppliesExtension_WhenAllowed()
    {
        Assert.Equal(D(2026, 4, 11),
            NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Day", 90, true, 10));
    }

    [Fact]
    public void ProbationEnd_IgnoresExtension_WhenNotAllowed()
    {
        Assert.Equal(D(2026, 4, 1),
            NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Day", 90, false, 10));
    }

    [Fact]
    public void ProbationEnd_Null_WhenNoStartOrZeroDuration()
    {
        Assert.Null(NotificationRuleGenerator.ProbationEnd(null, "Day", 90, false, 0));
        Assert.Null(NotificationRuleGenerator.ProbationEnd(D(2026, 1, 1), "Day", 0, false, 0));
    }

    // ─────────────── ربط اسم القاعدة ───────────────

    [Theory]
    [InlineData("عيد ميلاد موظف", NotificationRuleGenerator.RuleKind.Birthday)]
    [InlineData("ذكرى عمل موظف", NotificationRuleGenerator.RuleKind.Anniversary)]
    [InlineData("عقود الموظفين", NotificationRuleGenerator.RuleKind.ContractExpiry)]
    [InlineData("فترة التجربة", NotificationRuleGenerator.RuleKind.ProbationEnding)]
    public void MapRuleName_MapsSupportedRules(string name, NotificationRuleGenerator.RuleKind expected)
    {
        Assert.Equal(expected, NotificationRuleGenerator.MapRuleName(name));
    }

    [Fact]
    public void MapRuleName_Null_ForUnsupportedRule()
    {
        Assert.Null(NotificationRuleGenerator.MapRuleName("مخالفات الموظفين"));
        Assert.Null(NotificationRuleGenerator.MapRuleName("سن التقاعد"));
    }
}
