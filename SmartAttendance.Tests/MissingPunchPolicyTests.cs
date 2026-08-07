using System;
using System.Linq;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات الوجبة الثالثة: نافذة طلب البصمة المفقودة (دالة نقية)، وحجب أنواع مصادر
/// البيانات غير المنفَّذة.
/// </summary>
public class MissingPunchPolicyTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);

    // ===== النافذة الزمنية =====

    [Fact]
    public void Window_ZeroMeansNoLimit()
    {
        // الصفر = بلا حدّ، وهو الافتراضي — فلا يُرفض طلب كان يمرّ قبل الميزة.
        var ancient = new DateOnly(2020, 1, 1);

        Assert.True(MissingPunchPolicy.IsWithinWindow(ancient, Today, windowDays: 0));
    }

    [Theory]
    [InlineData(19, 7, true)]   // أمس — داخل نافذة 7
    [InlineData(14, 7, true)]   // على الحدّ تماماً (6 أيام)
    [InlineData(13, 7, true)]   // 7 أيام بالضبط — الحدّ شامل
    [InlineData(12, 7, false)]  // 8 أيام — خارج
    [InlineData(1, 7, false)]
    public void Window_EnforcesInclusiveBoundary(int punchDay, int windowDays, bool expected)
    {
        var punch = new DateOnly(2026, 3, punchDay);

        Assert.Equal(expected, MissingPunchPolicy.IsWithinWindow(punch, Today, windowDays));
    }

    [Fact]
    public void Window_TodayIsAlwaysAccepted()
    {
        Assert.True(MissingPunchPolicy.IsWithinWindow(Today, Today, windowDays: 1));
    }

    [Fact]
    public void Window_FutureDateIsAccepted()
    {
        // لا معنى لنافذةٍ للأمام — البصمة المستقبلية تُحكَم بقواعد أخرى لا بالنافذة.
        var future = Today.AddDays(3);

        Assert.True(MissingPunchPolicy.IsWithinWindow(future, Today, windowDays: 1));
    }

    [Fact]
    public void Window_CrossesMonthBoundaryByDaysNotByCalendar()
    {
        // 28 شباط مقابل 3 آذار = 3 أيام، لا «شهر مختلف».
        var punch = new DateOnly(2026, 2, 28);
        var today = new DateOnly(2026, 3, 3);

        Assert.True(MissingPunchPolicy.IsWithinWindow(punch, today, windowDays: 3));
        Assert.False(MissingPunchPolicy.IsWithinWindow(punch, today, windowDays: 2));
    }

    // ===== مصادر بيانات الحضور =====

    [Fact]
    public void OnlyExcelSourceIsImplemented()
    {
        Assert.True(AttendanceSourceStore.IsImplemented("Excel"));
        Assert.False(AttendanceSourceStore.IsImplemented("DbView"));
        Assert.False(AttendanceSourceStore.IsImplemented("Api"));
    }

    [Fact]
    public void SelectableReadTypes_ExcludeUnimplementedOnes()
    {
        // النوع بلا منفِّذ يجب ألا يُعرض بنموذج الإنشاء — وإلا أنشأ المستخدم مصدراً
        // يبدو مُهيَّأً ولا يقرأ شيئاً (إعداد صامت).
        var selectable = AttendanceSourceStore.SelectableReadTypes.Select(t => t.Key).ToList();

        Assert.Equal(new[] { "Excel" }, selectable);
    }

    [Fact]
    public void UnimplementedTypesKeepTheirLabelsForLegacyRows()
    {
        // محجوبة عن الإنشاء لكنها تبقى قابلة للتسمية، فلا يظهر مفتاح خام بشاشة
        // مصدرٍ أُنشئ قبل الحجب.
        Assert.Equal("عرض/جدول قاعدة بيانات", AttendanceSourceStore.ReadTypeLabel("DbView"));
        Assert.Equal("واجهة API خارجية", AttendanceSourceStore.ReadTypeLabel("Api"));
    }
}
