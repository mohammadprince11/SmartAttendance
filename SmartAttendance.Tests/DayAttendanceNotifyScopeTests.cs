using System;
using System.Collections.Generic;
using System.Linq;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// نطاق زرّ «إشعار الموظفين» بالحضور اليومي (الفجوة #7 — الجزء الذي كان مفتوحاً:
/// المُرسِل كان بـ/AttendanceViewer وحده).
///
/// <para><b>الثابت المحروس</b>: النطاق هو <b>العرض الحالي حرفياً</b> — نفس ما تراه
/// بالشاشة بعد الفلتر. ولذلك يمرّ العرضُ والزرُّ على
/// <see cref="DayAttendanceStore.FilterByStatus"/> نفسها. لو تفرّع المنطقان لصار
/// الزرّ يُرسل لمجموعةٍ غير المعروضة — عطلٌ صامت لا يظهر إلا بعد وصول الرسائل
/// لمن لا يعنيهم الأمر.</para>
///
/// <para>الدالّة نقيّة ولا تمسّ القاعدة، فالاختبار يشتغل بلا SQL Server.</para>
/// </summary>
public class DayAttendanceNotifyScopeTests
{
    private static DayAttendanceStore.DayRow Row(
        int employeeId, string status, bool stale = false, int day = 1) =>
        new()
        {
            EmployeeId = employeeId,
            EmployeeNo = $"E{employeeId:000}",
            WorkDate = new DateOnly(2026, 8, day),
            Status = status,
            IsStale = stale
        };

    private static readonly List<DayAttendanceStore.DayRow> Sample = new()
    {
        Row(1, "Present", day: 1),
        Row(1, "Late",    day: 2),
        Row(1, "Late",    day: 3),
        Row(2, "Late",    day: 1),
        Row(3, "Absent",  day: 1),
        Row(4, "Present", day: 1, stale: true)
    };

    [Fact]
    public void EmptyFilter_KeepsEveryRow()
    {
        Assert.Equal(Sample.Count, DayAttendanceStore.FilterByStatus(Sample, null).Count);
        Assert.Equal(Sample.Count, DayAttendanceStore.FilterByStatus(Sample, "").Count);
    }

    [Theory]
    [InlineData("Present", 2)]
    [InlineData("Late", 3)]
    [InlineData("Absent", 1)]
    [InlineData("Incomplete", 0)]
    public void StatusFilter_KeepsOnlyThatStatus(string status, int expected)
    {
        var filtered = DayAttendanceStore.FilterByStatus(Sample, status);

        Assert.Equal(expected, filtered.Count);
        Assert.All(filtered, row => Assert.Equal(status, row.Status));
    }

    /// <summary>
    /// «لم تُحلَّل» خاصية حداثة لا حالة حضور — فلا تُطابَق بـ<c>Status</c> وإلا
    /// أعادت صفراً دائماً (لا يومية حالتها النصّية «Stale»).
    /// </summary>
    [Fact]
    public void StaleFilter_MatchesFreshnessNotStatus()
    {
        var filtered = DayAttendanceStore.FilterByStatus(Sample, DayAttendanceStore.StaleFilterKey);

        Assert.Single(filtered);
        Assert.True(filtered[0].IsStale);
        Assert.Equal("Present", filtered[0].Status);   // حالتها حاضر، وبائتة رغم ذلك
    }

    /// <summary>
    /// المُشعَرون موظفون مميَّزون لا صفوف: المتأخر ثلاثة أيام يُشعَر مرّة واحدة.
    /// هذا هو الحساب الذي يقع بالشاشة وبالمعالج معاً.
    /// </summary>
    [Fact]
    public void NotifyScope_CountsDistinctEmployeesNotRows()
    {
        var lateRows = DayAttendanceStore.FilterByStatus(Sample, "Late");

        Assert.Equal(3, lateRows.Count);

        var recipients = lateRows.Select(row => row.EmployeeId).Distinct().ToList();

        Assert.Equal(2, recipients.Count);
        Assert.Contains(1, recipients);
        Assert.Contains(2, recipients);
    }

    /// <summary>مفتاح البائتة لا يصطدم بأي حالة حضور حقيقية.</summary>
    [Fact]
    public void StaleKey_NeverCollidesWithARealStatus()
    {
        string[] statuses = { "Present", "Late", "Absent", "Incomplete", "Weekend", "Rest" };

        Assert.DoesNotContain(DayAttendanceStore.StaleFilterKey, statuses);
    }
}
