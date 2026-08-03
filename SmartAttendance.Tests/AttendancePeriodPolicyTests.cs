using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار مالي: حدّ فترة الغلق يقرّر في أي شهرٍ يُحتسب يوم الحضور — وبالتالي في
/// أي مسير يدخل. خطأ يومٍ واحد هنا خطأ براتب.
/// </summary>
public class AttendancePeriodPolicyTests
{
    /// <summary>مثال محمد الحرفي: سياسة 21→20، وشهر تموز يعني 21/06 ← 20/07.</summary>
    [Fact]
    public void Policy_21_to_20_spans_previous_month()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 7, 21, 20);

        Assert.Equal(new DateOnly(2026, 6, 21), period.From);
        Assert.Equal(new DateOnly(2026, 7, 20), period.To);
        Assert.Equal(7, period.LabelMonth);
        Assert.Equal(2026, period.LabelYear);
    }

    /// <summary>التسمية تتبع الشهر صاحب الأيام الأكثر — 20 يوماً بتموز مقابل 10 بحزيران.</summary>
    [Fact]
    public void Label_follows_the_month_with_more_days()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 7, 21, 20);

        var daysInLabelMonth = 20;
        var daysInPreviousMonth = period.DayCount - daysInLabelMonth;

        Assert.True(daysInLabelMonth > daysInPreviousMonth);
        Assert.Equal(30, period.DayCount);   // 10 من حزيران + 20 من تموز
    }

    /// <summary>
    /// سياسة تجعل شهر التسمية هو **البداية** لا النهاية: 5→4 يعطي شهر التسمية
    /// 26 يوماً مقابل 4 للتالي، فالفترة تبدأ به لا تنتهي عنده.
    /// </summary>
    [Fact]
    public void When_label_month_has_more_days_as_start_the_period_starts_there()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 6, 5, 4);

        Assert.Equal(new DateOnly(2026, 6, 5), period.From);
        Assert.Equal(new DateOnly(2026, 7, 4), period.To);
        Assert.Equal(6, period.LabelMonth);
    }

    [Fact]
    public void Policy_within_one_month_does_not_span()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 7, 1, 31);

        Assert.Equal(new DateOnly(2026, 7, 1), period.From);
        Assert.Equal(new DateOnly(2026, 7, 31), period.To);
        Assert.Single(period.CoveredMonths());
    }

    /// <summary>شباط لا يملك يوم 30 — اليوم يُقصَر بدل أن يرمي استثناءً.</summary>
    [Theory]
    [InlineData(2026, 3, 30, 20, "2026-02-28", "2026-03-20")]
    [InlineData(2024, 3, 30, 20, "2024-02-29", "2024-03-20")]  // سنة كبيسة
    public void Day_is_clamped_to_short_months(
        int year, int month, int from, int to, string expectedFrom, string expectedTo)
    {
        var period = AttendancePeriodPolicy.Resolve(year, month, from, to);

        Assert.Equal(DateOnly.Parse(expectedFrom), period.From);
        Assert.Equal(DateOnly.Parse(expectedTo), period.To);
    }

    /// <summary>عبور رأس السنة: كانون الثاني يسحب من كانون الأول السابق.</summary>
    [Fact]
    public void Period_crosses_year_boundary()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 1, 21, 20);

        Assert.Equal(new DateOnly(2025, 12, 21), period.From);
        Assert.Equal(new DateOnly(2026, 1, 20), period.To);
    }

    /// <summary>
    /// التحليل يُبنى شهراً تقويمياً، فالفترة العابرة يجب أن تُبلّغ عن **شهرين** —
    /// وإلا بقي نصفها بلا يوميات وظهرت الشاشة ناقصةً بلا سبب ظاهر.
    /// </summary>
    [Fact]
    public void Spanning_period_covers_two_calendar_months()
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 7, 21, 20);

        Assert.Equal(new[] { (2026, 6), (2026, 7) }, period.CoveredMonths().ToArray());
    }

    /// <summary>سياسة غير صالحة ⟹ الشهر التقويمي كاملاً (سلوك ما قبل السياسات).</summary>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(21, 0)]
    [InlineData(32, 20)]
    public void Invalid_policy_falls_back_to_full_calendar_month(int from, int to)
    {
        var period = AttendancePeriodPolicy.Resolve(2026, 7, from, to);

        Assert.Equal(new DateOnly(2026, 7, 1), period.From);
        Assert.Equal(new DateOnly(2026, 7, 31), period.To);
    }
}
