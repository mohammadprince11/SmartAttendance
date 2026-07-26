using System;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقيّة لتوصيل إعداد «تعبئة البصمة المفقودة» (FillMissingCheckIn/Out) بمحرك
/// الاشتقاق <see cref="DayAttendanceStore.Derive"/> — كان صمّاءً (يُحفظ ولا يُقرأ). تضمن:
/// التعبئة تحدث فقط حين يوجد الطرف الآخر، محروسة بالراية (الافتراضي = لا تغيير)، لا تلفّق
/// يوماً كاملاً من لا بيانات، وتراعي المناوبة العابرة لمنتصف الليل.
/// </summary>
public class ShiftTypeFillMissingTests
{
    private static ShiftTypeStore.ShiftType Shift(
        bool fillIn = false, bool fillOut = false) => new()
    {
        Id = 1,
        IsFlexible = false,
        LatenessGraceMinutes = 0,
        EarlyLeaveGraceMinutes = 0,
        GraceExceededPolicy = "Subtract",
        FillMissingCheckIn = fillIn,
        FillMissingCheckOut = fillOut
    };

    private static ShiftTypeStore.ShiftDay Day(string? start = "09:00", string? end = "17:00") =>
        new() { DayIndex = 0, DayKind = "Work", StartTime = start, EndTime = end };

    private static DateTime At(int day, int hh, int mm) => new(2026, 7, day, hh, mm, 0);

    // ─────────────── تعبئة الخروج المفقود ───────────────

    [Fact]
    public void FillMissingCheckOut_On_FillsEndTime_AndComputesPresent()
    {
        var r = DayAttendanceStore.Derive(Shift(fillOut: true), Day(), "Work", At(6, 8, 55), null);

        Assert.Equal(At(6, 17, 0), r.CheckOut);
        Assert.Equal("Present", r.Status);
        Assert.Equal(0, r.EarlyLeaveHours);
        Assert.True(r.WorkedHours > 8m && r.WorkedHours < 8.2m);
    }

    [Fact]
    public void FillMissingCheckOut_Off_StaysIncomplete()
    {
        var r = DayAttendanceStore.Derive(Shift(fillOut: false), Day(), "Work", At(6, 8, 55), null);

        Assert.Null(r.CheckOut);
        Assert.Equal("Incomplete", r.Status);
        Assert.Equal(0, r.WorkedHours);
    }

    // ─────────────── تعبئة الدخول المفقود ───────────────

    [Fact]
    public void FillMissingCheckIn_On_FillsStartTime_NoLate_Present()
    {
        var r = DayAttendanceStore.Derive(Shift(fillIn: true), Day(), "Work", null, At(6, 17, 5));

        Assert.Equal(At(6, 9, 0), r.CheckIn);
        Assert.Equal(0, r.LateHours); // الدخول المعبّأ = بدء المناوبة تماماً ⟹ لا تأخير
        Assert.Equal("Present", r.Status);
    }

    [Fact]
    public void FillMissingCheckIn_Off_StaysAbsent()
    {
        var r = DayAttendanceStore.Derive(Shift(fillIn: false), Day(), "Work", null, At(6, 17, 5));

        Assert.Null(r.CheckIn);
        Assert.Null(r.CheckOut);
        Assert.Equal("Absent", r.Status);
    }

    // ─────────────── حراسة السلوك ───────────────

    [Fact]
    public void BothMissing_BothFlagsOn_StaysAbsent_NoFabrication()
    {
        var r = DayAttendanceStore.Derive(Shift(fillIn: true, fillOut: true), Day(), "Work", null, null);

        Assert.Null(r.CheckIn);
        Assert.Null(r.CheckOut);
        Assert.Equal("Absent", r.Status);
    }

    [Fact]
    public void BothPresent_FlagsOn_Unchanged()
    {
        var r = DayAttendanceStore.Derive(
            Shift(fillIn: true, fillOut: true), Day(), "Work", At(6, 9, 10), At(6, 17, 0));

        Assert.Equal(At(6, 9, 10), r.CheckIn);
        Assert.Equal(At(6, 17, 0), r.CheckOut);
        Assert.Equal("Late", r.Status); // 10 دقائق تأخير بلا سماح
    }

    [Fact]
    public void UnparseableShiftTime_FlagOn_FallsBackToAbsent()
    {
        var r = DayAttendanceStore.Derive(
            Shift(fillIn: true), Day(start: null), "Work", null, At(6, 17, 5));

        Assert.Null(r.CheckIn);
        Assert.Equal("Absent", r.Status);
    }

    [Fact]
    public void RestDay_FillFlagsOn_NotApplied()
    {
        var r = DayAttendanceStore.Derive(
            Shift(fillIn: true, fillOut: true), Day(), "Rest", null, null);

        Assert.Null(r.CheckIn);
        Assert.Equal("Rest", r.Status);
    }

    // ─────────────── العابرة لمنتصف الليل ───────────────

    [Fact]
    public void Overnight_FillCheckOut_LandsNextDay()
    {
        // مناوبة 22:00 → 06:00 (نهاية ≤ بداية)
        var r = DayAttendanceStore.Derive(
            Shift(fillOut: true), Day(start: "22:00", end: "06:00"), "Work", At(6, 21, 58), null);

        Assert.Equal(At(7, 6, 0), r.CheckOut); // اليوم التالي
        Assert.True(r.WorkedHours > 7.9m && r.WorkedHours < 8.1m);
    }

    [Fact]
    public void Overnight_FillCheckIn_LandsPreviousDay()
    {
        // خروج 06:05 باليوم التالي، الدخول مفقود ⟹ يُعبّأ بـ22:00 لليوم السابق
        var r = DayAttendanceStore.Derive(
            Shift(fillIn: true), Day(start: "22:00", end: "06:00"), "Work", null, At(7, 6, 5));

        Assert.Equal(At(6, 22, 0), r.CheckIn);
        Assert.True(r.WorkedHours > 8m && r.WorkedHours < 8.2m);
    }
}
