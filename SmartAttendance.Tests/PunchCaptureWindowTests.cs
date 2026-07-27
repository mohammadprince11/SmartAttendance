using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نافذة الالتقاط الزمنية للبصمات
/// (<see cref="DayAttendanceStore.TrySelectWindowPunches"/> و<see cref="DayAttendanceStore.HasCaptureWindow"/>) —
/// حدود TimeLimitFrom/To بمرساتَي اليوم السابق/التالي وتقسيم MidShiftTime للمناوبات الليلية.
/// دوال نقية فالتغطية رخيصة.
/// </summary>
public class PunchCaptureWindowTests
{
    private static readonly DateOnly D = new(2026, 7, 20);

    private static ShiftTypeStore.ShiftType Shift(
        string? from = null, bool fromDayBefore = false,
        string? to = null, bool toDayAfter = false, string? mid = null) => new()
        {
            Name = "اختبار",
            TimeLimitFrom = from,
            TimeLimitFromDayBefore = fromDayBefore,
            TimeLimitTo = to,
            TimeLimitToDayAfter = toDayAfter,
            MidShiftTime = mid
        };

    private static ShiftTypeStore.ShiftDay Day(string start, string end) =>
        new() { DayIndex = 0, DayKind = "Work", StartTime = start, EndTime = end };

    /// <summary>وقت على يوم الاختبار مزاحاً بأيام (لبصمات اليوم السابق/التالي).</summary>
    private static DateTime At(string time, int dayOffset = 0) =>
        D.AddDays(dayOffset).ToDateTime(TimeOnly.MinValue).Add(TimeSpan.Parse(time));

    private static (DateTime? In, DateTime? Out) Select(
        ShiftTypeStore.ShiftType shift, ShiftTypeStore.ShiftDay? day, params DateTime[] times)
    {
        var ordered = times.OrderBy(t => t).ToList();
        Assert.True(DayAttendanceStore.TrySelectWindowPunches(shift, day, D, ordered, out var selected));
        return selected;
    }

    // ===== وجود النافذة =====

    [Fact]
    public void HasCaptureWindow_NoLimits_False()
    {
        Assert.False(DayAttendanceStore.HasCaptureWindow(Shift()));
        Assert.False(DayAttendanceStore.HasCaptureWindow(Shift(mid: "13:00"))); // منتصف بلا حدود لا يفعّل النافذة
    }

    [Theory]
    [InlineData("06:00", null)]
    [InlineData(null, "20:00")]
    [InlineData("06:00", "20:00")]
    public void HasCaptureWindow_AnyLimit_True(string? from, string? to)
    {
        Assert.True(DayAttendanceStore.HasCaptureWindow(Shift(from: from, to: to)));
    }

    // ===== النافذة البسيطة (نفس اليوم) =====

    [Fact]
    public void Window_FiltersOutsidePunches()
    {
        // بصمة 05:00 قبل الحد و21:00 بعده ⇒ تسقطان؛ يبقى 08:00 دخولاً و17:00 خروجاً
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00"), Day("08:00", "16:00"),
            At("05:00"), At("08:00"), At("17:00"), At("21:00"));

        Assert.Equal(At("08:00"), checkIn);
        Assert.Equal(At("17:00"), checkOut);
    }

    [Fact]
    public void Window_SinglePunch_IsCheckInOnly()
    {
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00"), Day("08:00", "16:00"), At("08:10"));

        Assert.Equal(At("08:10"), checkIn);
        Assert.Null(checkOut);
    }

    [Fact]
    public void Window_NoPunches_ReturnsAbsent()
    {
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00"), Day("08:00", "16:00"), At("22:00"));

        Assert.Null(checkIn);
        Assert.Null(checkOut);
    }

    [Fact]
    public void Window_InvalidRange_FallsBack()
    {
        // نهاية ≤ بداية بنفس اليوم = نافذة غير صالحة ⇒ false (يبقى سلوك تاريخ الجهاز)
        Assert.False(DayAttendanceStore.TrySelectWindowPunches(
            Shift(from: "20:00", to: "06:00"), Day("08:00", "16:00"), D,
            new List<DateTime> { At("08:00") }, out _));
    }

    // ===== المراسي (اليوم السابق/التالي) للمناوبة الليلية =====

    [Fact]
    public void Overnight_DayAfterAnchor_CapturesNextDayCheckout()
    {
        // ليلية 22:00→06:00: الحد إلى 12:00 اليوم التالي يلتقط خروج 06:05 صباح الغد
        var (checkIn, checkOut) = Select(
            Shift(from: "18:00", to: "12:00", toDayAfter: true), Day("22:00", "06:00"),
            At("21:55"), At("06:05", dayOffset: 1));

        Assert.Equal(At("21:55"), checkIn);
        Assert.Equal(At("06:05", dayOffset: 1), checkOut);
    }

    [Fact]
    public void Overnight_DayBeforeAnchor_WindowStartsYesterday()
    {
        // حد البداية 20:00 من اليوم السابق: بصمة 23:00 أمس داخل النافذة
        var (checkIn, checkOut) = Select(
            Shift(from: "20:00", fromDayBefore: true, to: "10:00"), Day("22:00", "06:00"),
            At("23:00", dayOffset: -1), At("06:00"));

        Assert.Equal(At("23:00", dayOffset: -1), checkIn);
        Assert.Equal(At("06:00"), checkOut);
    }

    // ===== تقسيم منتصف المناوبة =====

    [Fact]
    public void MidShift_SplitsInAndOut()
    {
        // منتصف 12:00: بصمتا الصباح ⇒ الدخول أبكرهما؛ بصمتا المساء ⇒ الخروج أحدثهما
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00", mid: "12:00"), Day("08:00", "16:00"),
            At("08:00"), At("08:05"), At("15:55"), At("16:02"));

        Assert.Equal(At("08:00"), checkIn);
        Assert.Equal(At("16:02"), checkOut);
    }

    [Fact]
    public void MidShift_OnlyMorningPunches_NoCheckout()
    {
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00", mid: "12:00"), Day("08:00", "16:00"),
            At("08:00"), At("09:30"));

        Assert.Equal(At("08:00"), checkIn);
        Assert.Null(checkOut);
    }

    [Fact]
    public void MidShift_OnlyEveningPunches_NoCheckin()
    {
        var (checkIn, checkOut) = Select(
            Shift(from: "06:00", to: "20:00", mid: "12:00"), Day("08:00", "16:00"),
            At("15:00"), At("16:00"));

        Assert.Null(checkIn);
        Assert.Equal(At("16:00"), checkOut);
    }

    [Fact]
    public void MidShift_BeforeShiftStart_AnchorsNextDay()
    {
        // ليلية 22:00→06:00 بمنتصف 02:00 (أبكر من البداية ⇒ يُرسى بالغد):
        // دخول 21:50 الليلة وخروج 06:10 الغد يقعان على جانبَي المنتصف الصحيحَين
        var (checkIn, checkOut) = Select(
            Shift(from: "18:00", to: "12:00", toDayAfter: true, mid: "02:00"), Day("22:00", "06:00"),
            At("21:50"), At("06:10", dayOffset: 1));

        Assert.Equal(At("21:50"), checkIn);
        Assert.Equal(At("06:10", dayOffset: 1), checkOut);
    }

    [Fact]
    public void MidShift_OvernightDoubleMorningPunch_NotMistakenForCheckout()
    {
        // بصمتان متقاربتان قبل منتصف الليلية (دخول مكرر) ⇒ لا تُحسب الثانية خروجاً
        var (checkIn, checkOut) = Select(
            Shift(from: "18:00", to: "12:00", toDayAfter: true, mid: "02:00"), Day("22:00", "06:00"),
            At("21:50"), At("22:01"));

        Assert.Equal(At("21:50"), checkIn);
        Assert.Null(checkOut);
    }
}
