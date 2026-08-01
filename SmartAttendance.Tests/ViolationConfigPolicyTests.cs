using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تهيئة المخالفات.
///
/// أهمّها <see cref="ZeroDropMonths_MeansNeverDrops_PreservingTodayBehaviour"/>:
/// صفرٌ يجب أن يعني «لا تسقط أبداً» لا «تسقط فوراً» — والخلط بينهما كان سيُسقِط
/// **كل** العقوبات القائمة من تدرّج العقوبة لحظةَ نشر الميزة، بلا أن يطلب أحد ذلك.
///
/// و<see cref="CountableViolations_ExcludesDroppedOnes"/> يوثّق الأثر العملي
/// الوحيد للإسقاط: العقوبة الساقطة لا تُحذف ولا تُخفى، بل تخرج من العدّ.
/// </summary>
public class ViolationConfigPolicyTests
{
    private static readonly DateOnly Today = new(2026, 8, 1);

    // ── مهلة الاعتراض ──────────────────────────────────────────────────────────

    [Fact]
    public void ObjectionDeadline_CountsFromNotificationDate()
    {
        var deadline = ViolationConfigPolicy.ObjectionDeadline(new DateOnly(2026, 7, 28), 7);

        Assert.Equal(new DateOnly(2026, 8, 4), deadline);
    }

    [Fact]
    public void ZeroObjectionDays_MeansNoObjectionRight_NotExpired()
    {
        Assert.Null(ViolationConfigPolicy.ObjectionDeadline(new DateOnly(2026, 7, 28), 0));
        Assert.False(ViolationConfigPolicy.CanObject(new DateOnly(2026, 7, 28), 0, Today));
    }

    [Fact]
    public void WithoutNotification_ObjectionWindowNeverOpens()
    {
        // مهلةٌ تبدأ من تاريخ لم يقع بعد ليست مفتوحة — التبليغ شرطٌ لا تفصيل.
        Assert.Null(ViolationConfigPolicy.ObjectionDeadline(null, 7));
        Assert.False(ViolationConfigPolicy.CanObject(null, 7, Today));
    }

    [Fact]
    public void ObjectionWindow_IsInclusiveOfDeadlineDay()
    {
        var notified = new DateOnly(2026, 7, 25);

        Assert.True(ViolationConfigPolicy.CanObject(notified, 7, new DateOnly(2026, 8, 1)));
        Assert.False(ViolationConfigPolicy.CanObject(notified, 7, new DateOnly(2026, 8, 2)));
    }

    // ── إسقاط العقوبة ──────────────────────────────────────────────────────────

    [Fact]
    public void ZeroDropMonths_MeansNeverDrops_PreservingTodayBehaviour()
    {
        var decided = new DateOnly(2020, 1, 1);

        Assert.Null(ViolationConfigPolicy.DropDate(decided, 0));
        Assert.False(ViolationConfigPolicy.IsDropped(decided, 0, Today));
    }

    [Fact]
    public void DropDate_CountsFromDecisionDate()
    {
        Assert.Equal(
            new DateOnly(2026, 7, 1),
            ViolationConfigPolicy.DropDate(new DateOnly(2025, 7, 1), 12));
    }

    [Fact]
    public void IsDropped_IsTrueOnTheDropDateItself()
    {
        var decided = new DateOnly(2025, 8, 1);

        Assert.True(ViolationConfigPolicy.IsDropped(decided, 12, Today));
        Assert.False(ViolationConfigPolicy.IsDropped(decided, 12, new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public void UndecidedCase_NeverDrops()
    {
        Assert.False(ViolationConfigPolicy.IsDropped(null, 12, Today));
    }

    [Fact]
    public void CountableViolations_ExcludesDroppedOnes()
    {
        var decisions = new DateOnly?[]
        {
            new DateOnly(2022, 1, 1),  // ساقطة
            new DateOnly(2025, 7, 1),  // ساقطة (مرّ 13 شهراً)
            new DateOnly(2026, 3, 1),  // فعّالة
            null                        // قيد النظر — لا تُعدّ
        };

        Assert.Equal(1, ViolationConfigPolicy.CountableViolations(decisions, 12, Today));
        // بلا مدّة إسقاط تُعدّ كل المحسومة.
        Assert.Equal(3, ViolationConfigPolicy.CountableViolations(decisions, 0, Today));
    }

    // ── الترقيم والحالة ────────────────────────────────────────────────────────

    [Fact]
    public void ReferenceNumber_UsesTwoDigitYearAndResetsPerYear()
    {
        Assert.Equal("VC26-1", ViolationConfigPolicy.ReferenceNumber(null, 2026, 1));
        Assert.Equal("VC26-14", ViolationConfigPolicy.ReferenceNumber("VC", 2026, 14));
        Assert.Equal("MX27-1", ViolationConfigPolicy.ReferenceNumber("MX", 2027, 1));
    }

    [Fact]
    public void StatusLabel_PrefersDroppedOverEverythingElse()
    {
        var label = ViolationConfigPolicy.StatusLabel(
            notifiedOn: new DateOnly(2026, 7, 30),
            decidedOn: new DateOnly(2025, 1, 1),
            objectionDays: 7, dropMonths: 12, asOf: Today);

        Assert.Equal("أُسقِطت", label);
    }

    [Fact]
    public void StatusLabel_ShowsRemainingObjectionDays()
    {
        var label = ViolationConfigPolicy.StatusLabel(
            notifiedOn: new DateOnly(2026, 7, 30),
            decidedOn: null,
            objectionDays: 7, dropMonths: 0, asOf: Today);

        // بُلِّغ 07-30 ومهلته 7 أيام ⟹ آخر يوم 08-06، فالمتبقّي من 08-01 خمسة أيام.
        Assert.Equal("باب الاعتراض مفتوح (5 يوم)", label);
    }

    [Fact]
    public void StatusLabel_FinalAfterWindowCloses()
    {
        Assert.Equal("نهائية", ViolationConfigPolicy.StatusLabel(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), 7, 0, Today));

        Assert.Equal("قيد النظر", ViolationConfigPolicy.StatusLabel(
            new DateOnly(2026, 1, 1), null, 7, 0, Today));
    }
}
