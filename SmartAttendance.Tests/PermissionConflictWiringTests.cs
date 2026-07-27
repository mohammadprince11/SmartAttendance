using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات توصيل المغادرات بمحرك الحضور: قسيمة التأخير من المغادرة المعتمدة
/// (<see cref="DayAttendanceStore.PermissionLatenessCredit"/> ومعامل <c>lateCredit</c> في
/// <c>Derive</c>) واختيار الحركة الفعلية حول نافذة المغادرة
/// (<see cref="RecommendationStore.SelectMovementAround"/>). دوال نقية.
/// </summary>
public class PermissionConflictWiringTests
{
    private static TimeOnly T(string time) => TimeOnly.Parse(time);

    private static DateTime At(string time) =>
        new DateTime(2026, 7, 20).Add(TimeSpan.Parse(time));

    // ===== قسيمة التأخير =====

    [Fact]
    public void Credit_PermissionCoversLateness_FullCredit()
    {
        // مغادرة معتمدة 08:00–10:00 والدخول 10:00 ⇒ القسيمة ساعتان كاملتان
        var credit = DayAttendanceStore.PermissionLatenessCredit(
            new[] { (T("08:00"), T("10:00")) },
            shiftStart: T("08:00"), checkIn: T("10:00"),
            considerOutsideShift: false, shiftEnd: T("16:00"));

        Assert.Equal(TimeSpan.FromHours(2), credit);
    }

    [Fact]
    public void Credit_PermissionStartsBeforeShift_ClippedWhenNotConsidered()
    {
        // مغادرة 07:00–09:00 والدوام يبدأ 08:00: بلا «احتساب خارج المناوبة» تُقص لـ08:00–09:00
        var credit = DayAttendanceStore.PermissionLatenessCredit(
            new[] { (T("07:00"), T("09:00")) },
            shiftStart: T("08:00"), checkIn: T("09:30"),
            considerOutsideShift: false, shiftEnd: T("16:00"));

        Assert.Equal(TimeSpan.FromHours(1), credit);
    }

    [Fact]
    public void Credit_NoOverlapWithLateSpan_Zero()
    {
        // مغادرة عصراً لا تمس فترة التأخير الصباحية
        var credit = DayAttendanceStore.PermissionLatenessCredit(
            new[] { (T("13:00"), T("14:00")) },
            shiftStart: T("08:00"), checkIn: T("09:00"),
            considerOutsideShift: false, shiftEnd: T("16:00"));

        Assert.Equal(TimeSpan.Zero, credit);
    }

    [Fact]
    public void Credit_MultiplePermissions_Summed()
    {
        var credit = DayAttendanceStore.PermissionLatenessCredit(
            new[] { (T("08:00"), T("08:30")), (T("09:00"), T("09:30")) },
            shiftStart: T("08:00"), checkIn: T("10:00"),
            considerOutsideShift: false, shiftEnd: T("16:00"));

        Assert.Equal(TimeSpan.FromHours(1), credit);
    }

    [Fact]
    public void Credit_OnTimeArrival_Zero()
    {
        var credit = DayAttendanceStore.PermissionLatenessCredit(
            new[] { (T("08:00"), T("10:00")) },
            shiftStart: T("08:00"), checkIn: T("07:55"),
            considerOutsideShift: false, shiftEnd: T("16:00"));

        Assert.Equal(TimeSpan.Zero, credit);
    }

    [Fact]
    public void Derive_LateCredit_ErasesLateness()
    {
        var shift = new ShiftTypeStore.ShiftType { Name = "صباحية" };
        var day = new ShiftTypeStore.ShiftDay { DayIndex = 0, DayKind = "Work", StartTime = "09:00", EndTime = "17:00" };

        // متأخر ساعة لكن بقسيمة مغادرة ساعة ⇒ لا تأخير
        var row = DayAttendanceStore.Derive(shift, day, "Work",
            At("10:00"), At("17:00"), lateCredit: TimeSpan.FromHours(1));

        Assert.Equal(0m, row.LateHours);
        Assert.Equal("Present", row.Status);
    }

    [Fact]
    public void Derive_PartialLateCredit_LeavesRemainder()
    {
        var shift = new ShiftTypeStore.ShiftType { Name = "صباحية" };
        var day = new ShiftTypeStore.ShiftDay { DayIndex = 0, DayKind = "Work", StartTime = "09:00", EndTime = "17:00" };

        // متأخر ساعتين بقسيمة ساعة ⇒ تأخير ساعة
        var row = DayAttendanceStore.Derive(shift, day, "Work",
            At("11:00"), At("17:00"), lateCredit: TimeSpan.FromHours(1));

        Assert.Equal(1m, row.LateHours);
        Assert.Equal("Late", row.Status);
    }

    // ===== اختيار الحركة الفعلية حول نافذة المغادرة =====

    [Fact]
    public void Movement_OutAndBackAroundWindow_Selected()
    {
        // دخول 08:00، خروج 12:05 (المغادرة)، عودة 13:40، خروج نهائي 17:00
        var (left, returned) = RecommendationStore.SelectMovementAround(
            new[] { At("08:00"), At("12:05"), At("13:40"), At("17:00") }, At("12:00"));

        Assert.Equal(At("12:05"), left);
        Assert.Equal(At("13:40"), returned);
    }

    [Fact]
    public void Movement_NoMiddayPunches_EndOfDayOutNotReturned()
    {
        // يوم عادي (دخول/خروج فقط) ومغادرة منتصف اليوم بلا بصمات وسطية:
        // الخروج الأقرب = خروج نهاية الدوام، ولا «عودة» بعده ⇒ لا عودة تُقارن
        var (left, returned) = RecommendationStore.SelectMovementAround(
            new[] { At("08:00"), At("17:00") }, At("12:00"));

        Assert.Equal(At("17:00"), left);
        Assert.Null(returned);
    }

    [Fact]
    public void Movement_NoOutPunch_NothingSelected()
    {
        // بصمة دخول وحيدة ⇒ لا خروج يُقارن أصلاً
        var (left, returned) = RecommendationStore.SelectMovementAround(
            new[] { At("08:00") }, At("12:00"));

        Assert.Null(left);
        Assert.Null(returned);
    }

    [Fact]
    public void Movement_EarlyDeparture_PicksNearestOut()
    {
        // خرج 11:30 قبل النافذة 12:00 وعاد 13:00 — الخروج الأقرب للنافذة هو 11:30
        var (left, returned) = RecommendationStore.SelectMovementAround(
            new[] { At("08:00"), At("11:30"), At("13:00"), At("17:00") }, At("12:00"));

        Assert.Equal(At("11:30"), left);
        Assert.Equal(At("13:00"), returned);
    }
}
