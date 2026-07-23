using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات معيارية لمحرك اشتقاق اليوم <see cref="DayAttendanceStore.Derive"/> —
/// بؤرتها فترة السماح وسياسة تجاوزها (Subtract/Full)، وهي دالة نقية فالتغطية رخيصة.
/// </summary>
public class DayAttendanceDeriveTests
{
    private static ShiftTypeStore.ShiftType Shift(
        int latenessGrace = 0, int earlyGrace = 0, string policy = "Subtract") => new()
        {
            Name = "صباحية",
            LatenessGraceMinutes = latenessGrace,
            EarlyLeaveGraceMinutes = earlyGrace,
            GraceExceededPolicy = policy
        };

    private static ShiftTypeStore.ShiftDay Day(string start = "09:00", string end = "17:00") =>
        new() { DayIndex = 0, DayKind = "Work", StartTime = start, EndTime = end };

    private static DateTime At(string time) =>
        new DateTime(2026, 7, 20).Add(TimeSpan.Parse(time));

    // ===== فترة السماح للتأخير =====

    [Theory]
    [InlineData("09:03")]  // داخل السماحية
    [InlineData("09:15")]  // عندها بالضبط
    [InlineData("09:00")]  // بالوقت
    [InlineData("08:50")]  // مبكر
    public void Late_WithinGrace_IsZeroAndPresent(string checkIn)
    {
        var row = DayAttendanceStore.Derive(
            Shift(latenessGrace: 15), Day(), "Work", At(checkIn), At("17:00"));

        Assert.Equal(0, row.LateHours);
        Assert.Equal("Present", row.Status);
    }

    [Fact]
    public void Late_BeyondGrace_Subtract_RecordsDifferenceMinusGrace()
    {
        // 09:20 بسماحية 15 ⇒ 5 دقائق = 0.08 ساعة
        var row = DayAttendanceStore.Derive(
            Shift(latenessGrace: 15), Day(), "Work", At("09:20"), At("17:00"));

        Assert.Equal(0.08m, row.LateHours);
        Assert.Equal("Late", row.Status);
    }

    [Fact]
    public void Late_BeyondGrace_Full_RecordsWholeDifference()
    {
        // 09:20 بسماحية 15 وسياسة Full ⇒ 20 دقيقة = 0.33 ساعة
        var row = DayAttendanceStore.Derive(
            Shift(latenessGrace: 15, policy: "Full"), Day(), "Work", At("09:20"), At("17:00"));

        Assert.Equal(0.33m, row.LateHours);
        Assert.Equal("Late", row.Status);
    }

    [Fact]
    public void Late_NoGrace_KeepsLegacyBehaviour()
    {
        var row = DayAttendanceStore.Derive(Shift(), Day(), "Work", At("10:00"), At("17:00"));

        Assert.Equal(1m, row.LateHours);
        Assert.Equal("Late", row.Status);
    }

    // ===== فترة السماح للخروج المبكر =====

    [Theory]
    [InlineData("16:50")]  // داخل السماحية
    [InlineData("16:45")]  // عندها بالضبط
    [InlineData("17:30")]  // خروج متأخر
    public void EarlyLeave_WithinGrace_IsZero(string checkOut)
    {
        var row = DayAttendanceStore.Derive(
            Shift(earlyGrace: 15), Day(), "Work", At("09:00"), At(checkOut));

        Assert.Equal(0, row.EarlyLeaveHours);
    }

    [Fact]
    public void EarlyLeave_BeyondGrace_Subtract_RecordsDifferenceMinusGrace()
    {
        // 16:40 بسماحية 15 ⇒ 5 دقائق
        var row = DayAttendanceStore.Derive(
            Shift(earlyGrace: 15), Day(), "Work", At("09:00"), At("16:40"));

        Assert.Equal(0.08m, row.EarlyLeaveHours);
    }

    [Fact]
    public void EarlyLeave_BeyondGrace_Full_RecordsWholeDifference()
    {
        var row = DayAttendanceStore.Derive(
            Shift(earlyGrace: 15, policy: "Full"), Day(), "Work", At("09:00"), At("16:40"));

        Assert.Equal(0.33m, row.EarlyLeaveHours);
    }

    // ===== حالات أخرى للمحرك (تثبيت السلوك القائم) =====

    [Fact]
    public void MissingCheckIn_IsAbsent()
    {
        var row = DayAttendanceStore.Derive(Shift(latenessGrace: 15), Day(), "Work", null, null);

        Assert.Equal("Absent", row.Status);
        Assert.Equal(0, row.LateHours);
        Assert.Equal(0, row.WorkedHours);
    }

    [Fact]
    public void MissingCheckOut_IsIncomplete_ButLateStillDerived()
    {
        var row = DayAttendanceStore.Derive(
            Shift(latenessGrace: 15), Day(), "Work", At("09:20"), null);

        Assert.Equal("Incomplete", row.Status);
        Assert.Equal(0.08m, row.LateHours);
    }

    [Fact]
    public void OffDay_HasNoLateOrEarly_EvenBeyondGrace()
    {
        var row = DayAttendanceStore.Derive(
            Shift(latenessGrace: 15), Day(), "Weekend", At("10:00"), At("14:00"));

        Assert.Equal("Weekend", row.Status);
        Assert.Equal(0, row.LateHours);
        Assert.Equal(4m, row.WorkedHours);
    }

    [Fact]
    public void FlexibleShift_IgnoresGrace_UsesShortfall()
    {
        var shift = Shift(latenessGrace: 15);
        shift.IsFlexible = true;
        shift.FlexDailyHours = 8;

        var row = DayAttendanceStore.Derive(shift, Day(), "Work", At("10:00"), At("17:00"));

        Assert.Equal(0, row.LateHours);
        Assert.Equal(1m, row.EarlyLeaveHours);   // 7 ساعات عمل من 8 مطلوبة
        Assert.Equal("Present", row.Status);
    }

    // ===== الدالة المساعدة مباشرةً =====

    [Theory]
    [InlineData(0, 15, "Subtract", 0)]
    [InlineData(-10, 15, "Subtract", 0)]     // حضور مبكر
    [InlineData(15, 15, "Subtract", 0)]      // عند الحد
    [InlineData(16, 15, "Subtract", 0.02)]   // دقيقة واحدة بعده
    [InlineData(16, 15, "Full", 0.27)]
    [InlineData(75, 0, "Subtract", 1.25)]    // بلا سماحية
    [InlineData(75, 0, "Full", 1.25)]
    [InlineData(30, -5, "Subtract", 0.5)]    // سماحية سالبة تُعامل صفراً
    [InlineData(30, 15, null, 0.25)]         // سياسة غير محددة ⇒ الطرح
    public void ApplyGrace_Boundaries(int minutes, int grace, string? policy, double expected)
    {
        var value = DayAttendanceStore.ApplyGrace(TimeSpan.FromMinutes(minutes), grace, policy);

        Assert.Equal((decimal)expected, value);
    }
}
