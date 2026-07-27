using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات الجدولة السريعة بالروستر: «تعبئة الشهر من الأسبوع الأول» تحافظ على نمط
/// أيام الأسبوع بالإزاحة %7، و«نسخ الشهر الماضي» يحاذي يوم الأسبوع (قبل 28 يوماً).
/// </summary>
public class RosterQuickFillTests
{
    private static readonly DateOnly July = new(2026, 7, 1);

    [Theory]
    [InlineData(8, 1)]    // اليوم 8 ⟵ اليوم 1 (نفس يوم الأسبوع)
    [InlineData(14, 7)]
    [InlineData(15, 1)]
    [InlineData(31, 3)]   // 31-1=30، 30%7=2 ⟵ اليوم 3
    [InlineData(1, 1)]    // اليوم داخل الأسبوع الأول يرجع نفسه
    [InlineData(6, 6)]
    public void FirstWeekSource_ModuloSeven(int day, int expectedSourceDay) =>
        Assert.Equal(new DateOnly(2026, 7, expectedSourceDay),
            RosterStore.FirstWeekSource(July, new DateOnly(2026, 7, day)));

    [Fact]
    public void FirstWeekSource_PreservesWeekday()
    {
        for (var d = July; d <= new DateOnly(2026, 7, 31); d = d.AddDays(1))
            Assert.Equal(d.DayOfWeek, RosterStore.FirstWeekSource(July, d).DayOfWeek);
    }

    [Fact]
    public void PreviousMonthAligned_Exactly28DaysBack_SameWeekday()
    {
        for (var d = July; d <= new DateOnly(2026, 7, 31); d = d.AddDays(1))
        {
            var src = RosterStore.PreviousMonthAlignedSource(d);
            Assert.Equal(28, d.DayNumber - src.DayNumber);
            Assert.Equal(d.DayOfWeek, src.DayOfWeek);
        }
    }
}
