using System;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// نافذة تجميع الحضور تتبع «دورة الرواتب» لا الشهر التقويمي — بلاغ محمد: راتب تموز
/// كان يبتلع غياب 21/7→31/7 (يخصّ دورة آب). startDay=1 يجب أن يبقى تقويمياً حرفياً
/// (سلوك قديم)، و21 ينتج [21 من السابق .. 20 من الحالي].
/// </summary>
public class MonthAttendanceCycleWindowTests
{
    [Fact]
    public void StartDayOne_IsCalendarMonth_OldBehaviour()
    {
        var (from, to) = MonthAttendanceStore.CycleWindow(2026, 7, 1);
        Assert.Equal(new DateOnly(2026, 7, 1), from);
        Assert.Equal(new DateOnly(2026, 7, 31), to);
    }

    [Fact]
    public void StartDay21_July_Is21JuneTo20July()
    {
        var (from, to) = MonthAttendanceStore.CycleWindow(2026, 7, 21);
        Assert.Equal(new DateOnly(2026, 6, 21), from);
        Assert.Equal(new DateOnly(2026, 7, 20), to);
    }

    [Fact]
    public void StartDay21_January_CrossesYearBoundary()
    {
        var (from, to) = MonthAttendanceStore.CycleWindow(2026, 1, 21);
        Assert.Equal(new DateOnly(2025, 12, 21), from);
        Assert.Equal(new DateOnly(2026, 1, 20), to);
    }

    [Fact]
    public void StartDayIsClampedToMonthLength()
    {
        // 30 في فبراير 2026 (28 يوماً) ⟹ يُقصّ ليوم صالح، بلا استثناء.
        var ex = Record.Exception(() => MonthAttendanceStore.CycleWindow(2026, 2, 30));
        Assert.Null(ex);
    }
}
