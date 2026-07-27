using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>اختبارات تجريد دقائق الدلالات من ساعات الحضور (<see cref="DayAttendanceStore.StripSemanticHours"/>). نقي.</summary>
public class StripSemanticHoursTests
{
    [Theory]
    [InlineData(8.0, 60, 7.0)]     // ساعة استراحة من 8 ساعات
    [InlineData(8.0, 30, 7.5)]     // نصف ساعة
    [InlineData(8.0, 0, 8.0)]      // لا دلالات
    [InlineData(8.0, -15, 8.0)]    // قيمة سالبة تُتجاهل
    [InlineData(1.0, 90, 0.0)]     // التجريد أكبر من الساعات ⇒ صفر لا سالب
    [InlineData(8.0, 45, 7.25)]    // 45 دقيقة = 0.75
    public void Strip_Cases(decimal worked, int minutes, decimal expected)
    {
        Assert.Equal(expected, DayAttendanceStore.StripSemanticHours(worked, minutes));
    }
}
