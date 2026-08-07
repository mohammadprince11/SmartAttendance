using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// أيام العمل خارج المكتب: «عمل من المنزل» (<c>Remote</c>) و«رحلة عمل»
/// (<c>BusinessTrip</c>) — سياقا يومٍ يُنتَجان من طلبٍ معتمد بالخدمة الذاتية.
///
/// <para><b>القرار المحروس</b> (محمد، 2026-08-07): الأثر المالي <b>محايد</b>. المحرّك
/// يسجّل السياق ولا يمنح إعفاءً — فيوم العمل عن بُعد يُشتقّ <b>تماماً كيوم عمل</b>:
/// بصماته تعطي حاضراً/متأخراً/ناقصاً، وبلا بصمة يبقى <b>غياباً</b>. من أراد غير ذلك
/// بناه قاعدةً بمنشئ القواعد.</para>
///
/// <para>ولهذا يجب أن تفشل هذه الاختبارات إن تسلّل إعفاءٌ ضمنيّ للمحرّك لاحقاً —
/// وهو أكثر ما يُغري بالتعديل هنا: «طبعاً يوم العمل من المنزل ليس غياباً».</para>
/// </summary>
public class OutOfOfficeDayKindTests
{
    private static ShiftTypeStore.ShiftType Shift() => new()
    {
        Name = "صباحية",
        LatenessGraceMinutes = 0,
        EarlyLeaveGraceMinutes = 0,
        GraceExceededPolicy = "Subtract"
    };

    private static ShiftTypeStore.ShiftDay Day() =>
        new() { DayIndex = 0, DayKind = "Work", StartTime = "09:00", EndTime = "17:00" };

    private static DateTime At(string time) =>
        new DateTime(2026, 8, 10).Add(TimeSpan.Parse(time));

    // ═══ تصنيف السياق ═══

    [Theory]
    [InlineData("Work")]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void WorkingKinds_AreDerivedWithFullWorkdayRules(string dayKind) =>
        Assert.True(DayAttendanceStore.IsWorkingKind(dayKind));

    [Theory]
    [InlineData("Weekend")]
    [InlineData("Rest")]
    public void OffKinds_AreNotWorkingKinds(string dayKind) =>
        Assert.False(DayAttendanceStore.IsWorkingKind(dayKind));

    // ═══ الأثر المحايد — جوهر القرار ═══

    /// <summary>بلا بصمة يبقى اليوم غياباً رغم اعتماد العمل من المنزل.</summary>
    [Theory]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void NoPunch_StaysAbsent_EvenWhenApproved(string dayKind)
    {
        var row = DayAttendanceStore.Derive(Shift(), Day(), dayKind, null, null);

        Assert.Equal("Absent", row.Status);
        Assert.Equal(0, row.WorkedHours);
    }

    /// <summary>والتأخير يُحتسب كما لو كان بالمكتب — لا إعفاء ضمنيّ.</summary>
    [Theory]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void LatePunch_IsStillLate(string dayKind)
    {
        var row = DayAttendanceStore.Derive(Shift(), Day(), dayKind, At("10:00"), At("17:00"));

        Assert.Equal("Late", row.Status);
        Assert.Equal(1, row.LateHours);
    }

    /// <summary>يومٌ مكتمل يُطابق تماماً نظيره بيوم العمل — قيمةً بقيمة.</summary>
    [Theory]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void FullDay_MatchesTheOfficeDayExactly(string dayKind)
    {
        var office = DayAttendanceStore.Derive(Shift(), Day(), "Work", At("09:00"), At("17:00"));
        var remote = DayAttendanceStore.Derive(Shift(), Day(), dayKind, At("09:00"), At("17:00"));

        Assert.Equal(office.Status, remote.Status);
        Assert.Equal(office.LateHours, remote.LateHours);
        Assert.Equal(office.EarlyLeaveHours, remote.EarlyLeaveHours);
        Assert.Equal(office.WorkedHours, remote.WorkedHours);
    }

    /// <summary>
    /// وبالمقابل: العطلة والراحة تبقيان بلا أحكام. لو ابتلع تعديلٌ لاحق السياقين
    /// الجديدين في فرع «غير يوم عمل» لأعاد هذا «Rest» ولفقد الغياب والتأخير.
    /// </summary>
    [Fact]
    public void OffDays_KeepTheirNoJudgementBranch()
    {
        var weekend = DayAttendanceStore.Derive(Shift(), Day(), "Weekend", At("10:00"), At("17:00"));

        Assert.Equal("Weekend", weekend.Status);
        Assert.Equal(0, weekend.LateHours);
        Assert.Equal(7, weekend.WorkedHours);
    }

    // ═══ ربط نوع الطلب بسياق اليوم ═══

    [Theory]
    [InlineData("WorkFromHome", "Remote")]
    [InlineData("BusinessTrip", "BusinessTrip")]
    public void RequestType_MapsToItsDayKind(string requestType, string expected)
    {
        Assert.True(DayAttendanceStore.RequestTypeDayKinds.TryGetValue(requestType, out var dayKind));
        Assert.Equal(expected, dayKind);
    }

    /// <summary>الربط غير حسّاس لحالة الأحرف — قيمة نصّية تأتي من عمود قاعدة.</summary>
    [Fact]
    public void RequestTypeMap_IsCaseInsensitive() =>
        Assert.True(DayAttendanceStore.RequestTypeDayKinds.ContainsKey("workfromhome"));

    /// <summary>الإجازة والمغادرة ليستا سياقَي مكان — لا تُنتجان DayKind.</summary>
    [Theory]
    [InlineData("Leave")]
    [InlineData("ExitPermission")]
    [InlineData("MissingPunch")]
    public void UnrelatedRequestTypes_ProduceNoDayKind(string requestType) =>
        Assert.False(DayAttendanceStore.RequestTypeDayKinds.ContainsKey(requestType));

    // ═══ منشئ القواعد ═══

    /// <summary>
    /// كل سياق يُنتجه التحليل يجب أن يكون قابلاً للاستهداف بقاعدة — وإلا عاد
    /// «الخيار الصامت» من الباب الآخر: سياقٌ يُكتب بالقاعدة ولا أحد يستطيع الحكم عليه.
    /// </summary>
    [Theory]
    [InlineData("Remote")]
    [InlineData("BusinessTrip")]
    public void EveryProducedContext_IsTargetableByARule(string dayKind) =>
        Assert.Contains(ShiftRuleStore.ApplyOnOptions, option => option.Key == dayKind);

    /// <summary>
    /// والعكس: `BusinessTrip` كان خياراً بلا منتِج — أيّ قاعدة عليه لا تنطبق أبداً.
    /// هذا يحرس ألّا يعود سياقٌ بمنشئ القواعد بلا منتِج بالتحليل.
    /// </summary>
    [Fact]
    public void NoRuleContext_IsLeftWithoutAProducer()
    {
        // السياقات التي ينتجها المحرّك: أنواع أيام المناوبة · حالات مشتقّة · طلبات معتمدة.
        var produced = new HashSet<string>
        {
            "All", "Work", "Weekend", "Rest", "Holiday", "Leave"
        };

        foreach (var dayKind in DayAttendanceStore.RequestTypeDayKinds.Values)
        {
            produced.Add(dayKind);
        }

        var orphans = ShiftRuleStore.ApplyOnOptions
            .Select(option => option.Key)
            .Where(key => !produced.Contains(key))
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "سياقات معروضة بمنشئ القواعد بلا منتِج بالتحليل — قواعدها لن تنطبق أبداً: "
            + string.Join(" · ", orphans));
    }
}
