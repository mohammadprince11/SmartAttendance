using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات انحدار لـ«سعر ساعة العمل الإضافي بسياق اليوم» (الفجوة #6 بدراسة كيان).
///
/// <para>قبل هذه الميزة كان <c>AttendanceTransactionStore.TransferToPayrollAsync</c>
/// يختم كل ساعة أوفرتايم بـ<c>PayrollTransactionStore.DefaultRateFactor</c> ثابتاً،
/// سواء كُسبت بيوم عملٍ عاديّ أو بجمعةٍ أو بعطلةٍ رسمية.</para>
///
/// <para><b>الضمانة الأولى وأهمّها</b>: مناوبةٌ لم تُملأ حقولها تعطي المعامل الافتراضي
/// <b>بالضبط</b> — فالهجرة والكود لا يغيّران رقماً واحداً بأي مسير قائم، وتفعيل
/// السلوك الجديد قرارٌ صريح لكل مناوبة. هذا ما تفرضه قاعدة «لا تغيير بالصيغ إلا
/// بطلب صريح» بـCLAUDE.md، وهو أوّل ما يجب أن يفشل إن انكسر.</para>
///
/// <para>الدالّة نقيّة فالتغطية رخيصة — وهي حسابٌ ماليّ يستحقّها.</para>
/// </summary>
public class OvertimeRateByDayContextTests
{
    private static ShiftTypeStore.ShiftType Shift(
        decimal? weekend = null, decimal? rest = null,
        decimal? holiday = null, decimal? leave = null) => new()
        {
            Name = "صباحية",
            OvertimeRateWeekend = weekend,
            OvertimeRateRest = rest,
            OvertimeRateHoliday = holiday,
            OvertimeRateLeave = leave
        };

    private static DayAttendanceStore.DayRow Day(string dayKind, string status) =>
        new() { DayKind = dayKind, Status = status };

    // ═══ ضمانة عدم التغيير ═══

    [Theory]
    [InlineData("Work")]
    [InlineData("Weekend")]
    [InlineData("Rest")]
    [InlineData("Holiday")]
    [InlineData("Leave")]
    public void UnconfiguredShift_KeepsTheDefaultFactor_InEveryContext(string context) =>
        Assert.Equal(
            PayrollTransactionStore.DefaultRateFactor,
            ShiftTypeStore.ResolveOvertimeFactor(Shift(), context));

    /// <summary>ولا مناوبة أصلاً (يومية بلا مناوبة) ⟹ الافتراضي، لا استثناء.</summary>
    [Fact]
    public void NullShift_KeepsTheDefaultFactor() =>
        Assert.Equal(
            PayrollTransactionStore.DefaultRateFactor,
            ShiftTypeStore.ResolveOvertimeFactor(null, "Weekend"));

    // ═══ كل سياق يقرأ حقله ═══

    [Fact]
    public void EachContext_ReadsItsOwnField()
    {
        var shift = Shift(weekend: 2.0m, rest: 2.25m, holiday: 3m, leave: 1.25m);

        Assert.Equal(2.0m, ShiftTypeStore.ResolveOvertimeFactor(shift, "Weekend"));
        Assert.Equal(2.25m, ShiftTypeStore.ResolveOvertimeFactor(shift, "Rest"));
        Assert.Equal(3m, ShiftTypeStore.ResolveOvertimeFactor(shift, "Holiday"));
        Assert.Equal(1.25m, ShiftTypeStore.ResolveOvertimeFactor(shift, "Leave"));
    }

    /// <summary>
    /// يوم العمل العاديّ لا معامل خاصّ له — لا بكيان ولا عندنا. ضبطُ الحقول الأربعة
    /// يجب ألّا يسرّب قيمةً إليه، وإلا تغيّر أجر الأوفرتايم اليوميّ بلا طلب.
    /// </summary>
    [Theory]
    [InlineData("Work")]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void WorkingDayContexts_StayOnTheDefault_EvenWhenOthersAreSet(string context)
    {
        var shift = Shift(weekend: 2m, rest: 2m, holiday: 3m, leave: 3m);

        Assert.Equal(
            PayrollTransactionStore.DefaultRateFactor,
            ShiftTypeStore.ResolveOvertimeFactor(shift, context));
    }

    // ═══ الصفر ليس «غير محدَّد» ═══

    /// <summary>
    /// صفر = «لا أجر إضافيّ لهذه الساعة» وهو قرار صالح. معاملته كـ«غير محدَّد»
    /// يقلبه إلى 1.5 — أي يدفع أجراً أمر المستخدم بألّا يُدفع.
    /// </summary>
    [Fact]
    public void ZeroIsHonoured_NotTreatedAsUnset() =>
        Assert.Equal(0m, ShiftTypeStore.ResolveOvertimeFactor(Shift(holiday: 0m), "Holiday"));

    /// <summary>والسالب يُهمَل: لا يقلب إشارة الأجر بل يسقط للافتراضي.</summary>
    [Fact]
    public void NegativeIsIgnored_FallsBackToDefault() =>
        Assert.Equal(
            PayrollTransactionStore.DefaultRateFactor,
            ShiftTypeStore.ResolveOvertimeFactor(Shift(weekend: -2m), "Weekend"));

    // ═══ اتساق السياق مع محرّك القواعد ═══

    /// <summary>
    /// السياق يُحسم بـ<see cref="ShiftRuleStore.EffectiveContext"/> نفسها التي تحسم
    /// انطباق القواعد. فحالةُ العطلة الرسمية تغلب نوعَ اليوم — وإلا انطبقت قاعدة
    /// «العطلة الرسمية» على اليوم بينما احتُسب أجره بمعامل «يوم العمل».
    /// </summary>
    [Fact]
    public void HolidayStatus_OverridesWorkDayKind_ForBothRulesAndPay()
    {
        var day = Day(dayKind: "Work", status: "Holiday");
        var context = ShiftRuleStore.EffectiveContext(day);

        Assert.Equal("Holiday", context);
        Assert.Equal(3m, ShiftTypeStore.ResolveOvertimeFactor(Shift(holiday: 3m), context));
    }

    [Fact]
    public void LeaveStatus_OverridesWorkDayKind()
    {
        var context = ShiftRuleStore.EffectiveContext(Day(dayKind: "Work", status: "Leave"));

        Assert.Equal("Leave", context);
        Assert.Equal(1.25m, ShiftTypeStore.ResolveOvertimeFactor(Shift(leave: 1.25m), context));
    }

    /// <summary>وبلا حالةٍ غالبة يعود السياق لنوع اليوم.</summary>
    [Theory]
    [InlineData("Weekend", "Present")]
    [InlineData("Rest", "Present")]
    [InlineData("Work", "Late")]
    public void WithoutAnOverridingStatus_ContextIsTheDayKind(string dayKind, string status) =>
        Assert.Equal(dayKind, ShiftRuleStore.EffectiveContext(Day(dayKind, status)));

    /// <summary>
    /// كل سياقٍ يعرفه محرّك القواعد ويمكن أن تُكسَب فيه ساعة يجب أن يُحسم معامله بلا
    /// استثناء — الحارس هنا أن الدالّة كلّية: لا سياق يُسقطها.
    /// </summary>
    [Fact]
    public void ResolverIsTotal_OverEveryRuleContext()
    {
        foreach (var (key, _) in ShiftRuleStore.ApplyOnOptions)
        {
            var factor = ShiftTypeStore.ResolveOvertimeFactor(Shift(weekend: 2m), key);

            Assert.True(factor >= 0, $"السياق «{key}» أعطى معاملاً سالباً.");
        }
    }
}
