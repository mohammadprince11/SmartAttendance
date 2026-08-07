using System;
using System.Linq;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تعداد أيام فترة الغلق — الأساس الذي تُبنى عليه أعمدة مستعرض الحضور
/// بعد أن صار يتبع سياسة القفل بدل الشهر التقويمي.
/// </summary>
public class AttendancePeriodDaysTests
{
    [Fact]
    public void EachDay_CoversWholeCalendarMonth_WhenNoPolicy()
    {
        // بلا سياسة: 1 → آخر الشهر (سلوك ما قبل السياسات).
        var period = AttendancePeriodPolicy.Resolve(2026, 3, 1, 31);
        var days = period.EachDay().ToList();

        Assert.Equal(31, days.Count);
        Assert.Equal(new DateOnly(2026, 3, 1), days.First());
        Assert.Equal(new DateOnly(2026, 3, 31), days.Last());
        Assert.Equal(period.DayCount, days.Count);
    }

    [Fact]
    public void EachDay_CrossesMonthBoundary_ForCutoffPeriod()
    {
        // سياسة 21 → 20: الفترة تعبر حدّ الشهر.
        var period = AttendancePeriodPolicy.Resolve(2026, 8, 21, 20);
        var days = period.EachDay().ToList();

        Assert.Equal(period.From, days.First());
        Assert.Equal(period.To, days.Last());
        Assert.Equal(period.DayCount, days.Count);

        // تلمس شهرين مختلفين
        Assert.True(days.Select(d => d.Month).Distinct().Count() == 2);
    }

    [Fact]
    public void EachDay_DayNumbersHappenToBeUnique_ButDatesAreTheHonestKey()
    {
        // فترة الغلق القياسية (from > to) = [from..آخر الشهر] + [1..to]، ولأن
        // to < from لا يتقاطع الطرفان ⟹ أرقام الأيام فريدة **بالمصادفة**.
        // الفهرسة بالتاريخ ليست إصلاح تصادمٍ قائم، بل إزالة اعتماد على هذه
        // المصادفة: أي فترة أطول من شهر (أو تعريف مستقبليّ مختلف) تكسر ترقيم اليوم،
        // بينما التاريخ لا يلتبس أبداً.
        var period = AttendancePeriodPolicy.Resolve(2026, 8, 21, 20);
        var days = period.EachDay().ToList();

        Assert.Equal(days.Count, days.Select(d => d.Day).Distinct().Count());
        Assert.Equal(days.Count, days.Distinct().Count());
    }

    [Fact]
    public void DayNumberKey_BreaksOnPeriodsLongerThanAMonth()
    {
        // البرهان على أن ترقيم اليوم اعتمادٌ هشّ: فترة تتجاوز الشهر تكرّر الرقم.
        var from = new DateOnly(2026, 7, 21);
        var to = new DateOnly(2026, 9, 20);
        var period = new AttendancePeriodPolicy.Period(from, to, 2026, 8);

        var days = period.EachDay().ToList();

        Assert.True(days.Select(d => d.Day).Distinct().Count() < days.Count);
        Assert.Equal(days.Count, days.Distinct().Count());   // التاريخ يبقى فريداً
    }

    [Fact]
    public void EachDay_IsContiguousAndAscending()
    {
        var days = AttendancePeriodPolicy.Resolve(2026, 8, 21, 20).EachDay().ToList();

        for (var i = 1; i < days.Count; i++)
        {
            Assert.Equal(days[i - 1].DayNumber + 1, days[i].DayNumber);
        }
    }

    [Fact]
    public void EachDay_HandlesLeapFebruary()
    {
        var days = AttendancePeriodPolicy.Resolve(2024, 2, 1, 29).EachDay().ToList();

        Assert.Equal(29, days.Count);
        Assert.Equal(new DateOnly(2024, 2, 29), days.Last());
    }
}
