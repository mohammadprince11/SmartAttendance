using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// رسم «حضور الأسبوع». القاعدة الحاكمة: يوم بلا أيام محلَّلة يُعرض **«—» لا 0%** —
/// «لم يحضر أحد» ادّعاء مختلف تماماً عن «لم يُحلَّل بعد».
/// </summary>
public class WeeklyAttendanceSeriesTests
{
    private static readonly DateOnly Today = new(2026, 7, 31);

    private static IReadOnlyList<WeeklyAttendanceSeries.DayBar> Build(
        params WeeklyAttendanceSeries.DayCounts[] counts) =>
        WeeklyAttendanceSeries.Build(counts, Today);

    [Fact]
    public void AlwaysRendersSevenDaysEndingToday()
    {
        var bars = Build();

        Assert.Equal(7, bars.Count);
        Assert.Equal(Today.AddDays(-6), bars[0].Date);
        Assert.Equal(Today, bars[^1].Date);
    }

    [Fact]
    public void OnlyTheLastColumnIsMarkedToday()
    {
        var bars = Build();

        Assert.Single(bars, b => b.IsToday);
        Assert.True(bars[^1].IsToday);
        Assert.Equal("اليوم", bars[^1].Label);
    }

    [Fact]
    public void PercentIsPresentOverAnalyzed()
    {
        var bars = Build(new WeeklyAttendanceSeries.DayCounts(Today, Present: 88, Analyzed: 100));

        Assert.Equal(88, bars[^1].Percent);
        Assert.Equal("88%", bars[^1].Display);
    }

    [Fact]
    public void PercentIsRounded()
    {
        var bars = Build(new WeeklyAttendanceSeries.DayCounts(Today, Present: 2, Analyzed: 3));

        Assert.Equal(67, bars[^1].Percent);
    }

    // ===== الصدق: «—» لا صفر =====

    [Fact]
    public void DayWithoutRows_ShowsDashNotZero()
    {
        var bar = Build()[^1];

        Assert.Null(bar.Percent);
        Assert.Equal("—", bar.Display);
        Assert.False(bar.HasData);
    }

    [Fact]
    public void DayWithZeroAnalyzed_ShowsDashNotZero()
    {
        var bars = Build(new WeeklyAttendanceSeries.DayCounts(Today, Present: 0, Analyzed: 0));

        Assert.Equal("—", bars[^1].Display);
    }

    /// <summary>غياب حقيقي (محلَّل وصفر حاضر) يجب أن يظهر 0% لا «—».</summary>
    [Fact]
    public void AnalyzedDayWithNobodyPresent_ShowsZeroPercent()
    {
        var bars = Build(new WeeklyAttendanceSeries.DayCounts(Today, Present: 0, Analyzed: 120));

        Assert.Equal(0, bars[^1].Percent);
        Assert.Equal("0%", bars[^1].Display);
        Assert.True(bars[^1].HasData);
    }

    // ===== الارتفاع =====

    [Fact]
    public void EmptyDay_KeepsAFaintStub() =>
        Assert.Equal(6, Build()[^1].BarHeight);

    [Fact]
    public void ZeroPercentDay_StillRendersAMinimumBar() =>
        Assert.Equal(4, Build(
            new WeeklyAttendanceSeries.DayCounts(Today, 0, 120))[^1].BarHeight);

    [Fact]
    public void FullDay_IsCappedAtFullHeight() =>
        Assert.Equal(100, Build(
            new WeeklyAttendanceSeries.DayCounts(Today, 120, 120))[^1].BarHeight);

    [Fact]
    public void CountsOutsideTheWindow_AreIgnored()
    {
        var bars = Build(new WeeklyAttendanceSeries.DayCounts(Today.AddDays(-30), 50, 50));

        Assert.All(bars, b => Assert.False(b.HasData));
    }
}
