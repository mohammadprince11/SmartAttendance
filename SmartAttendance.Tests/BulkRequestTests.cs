using SmartAttendance.Domain.Enums;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Pages.DayAttendance;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// التقديم الجماعي للطلبات من شاشة الحضور اليومي (<see cref="BulkRequestStore"/>).
///
/// <para><b>لماذا اختبارات انحدار إلزامية هنا</b>: القرار المعتمد (محمد، 2026-08-07)
/// أن الطلب المقدَّم من HR <b>يُعتمد فوراً بلا لجنة</b> — أي كتابةٌ تخصم أرصدة إجازات
/// وتدخل المسير بلا مراجعة بشرية. فالقواعد الحمراء تفرض حراسة: الرصيد، ومنع الخصم
/// المزدوج، وأنّ النوع لا يُكتب صفّاً ميتاً لا يقرؤه محرّك.</para>
/// </summary>
public class BulkRequestTests
{
    private static RequestTypeStore.ReqType Type(
        string name, string paidMode = "full", int? allowedDays = null, int? maxPerRequest = null) =>
        new()
        {
            Id = 1,
            Name = name,
            PaidMode = paidMode,
            AllowedDays = allowedDays,
            MaxPerRequest = maxPerRequest
        };

    private static DateOnly D(int day) => new(2026, 8, day);

    private static BulkRequestStore.Candidate Candidate(
        IReadOnlyList<(DateOnly From, DateOnly To)> runs,
        bool matches = true,
        bool overlap = false,
        decimal remaining = 100m) =>
        new(1, "موظف", runs, matches, overlap, remaining);

    // ═══ الأثر: النوع ⟵ ما تقرؤه المحرّكات ═══

    [Theory]
    [InlineData("إجازة سنوية", LeaveType.Annual)]
    [InlineData("إجازة مرضية", LeaveType.Sick)]
    [InlineData("إجازة وفاة", LeaveType.Emergency)]
    public void LeaveNames_MapToDomainLeaveType(string name, LeaveType expected)
    {
        var effect = BulkRequestStore.ResolveEffect(Type(name));

        Assert.Equal(BulkRequestStore.EffectKind.Leave, effect.Kind);
        Assert.Equal(expected, effect.Leave);
    }

    [Fact]
    public void UnpaidLeave_MapsToUnpaid_SoPayrollDeductsIt()
    {
        var effect = BulkRequestStore.ResolveEffect(Type("إجازة غير مدفوعة", paidMode: "unpaid"));

        Assert.Equal(LeaveType.Unpaid, effect.Leave);
    }

    [Fact]
    public void UnpaidDeparture_IsExitPermission_NotLeave()
    {
        // مزلق التسمية: «مغادرة غير مدفوعة» تحمل ضوابط الإجازة غير المدفوعة، فلو
        // فُحص «إجازة» أولاً لخُصمت يوماً كاملاً بدل أن تكون نافذةً زمنية.
        var effect = BulkRequestStore.ResolveEffect(Type("مغادرة غير مدفوعة", paidMode: "unpaid"));

        Assert.Equal(BulkRequestStore.EffectKind.ExitPermission, effect.Kind);
        Assert.Equal("ExitPermission", effect.RequestTypeCode);
        Assert.Null(effect.Leave);
    }

    [Fact]
    public void BusinessTask_MapsToBusinessTripDayKind()
    {
        var effect = BulkRequestStore.ResolveEffect(Type("مهمة عمل"));

        Assert.Equal(BulkRequestStore.EffectKind.OutOfOffice, effect.Kind);
        Assert.Equal("BusinessTrip", effect.RequestTypeCode);
    }

    [Fact]
    public void TypeWithoutConsumer_HasNoEffect_SoItIsRefusedNotWrittenDead()
    {
        // الأوفرتايم يُشتقّ من البصمات؛ لا محرّك يقرأ طلباً بهذا النوع. كتابته هنا
        // كانت ستُنتج صفّاً يظنّه المستخدم مطبَّقاً وهو بلا أثر.
        Assert.Equal(
            BulkRequestStore.EffectKind.None,
            BulkRequestStore.ResolveEffect(Type("عمل إضافي")).Kind);
    }

    // ═══ دمج أيام التحديد ═══

    [Fact]
    public void ConsecutiveDays_MergeIntoOneRun()
    {
        var runs = BulkRequestStore.MergeRuns(new[] { D(3), D(1), D(2) });

        Assert.Single(runs);
        Assert.Equal((D(1), D(3)), runs[0]);
        Assert.Equal(3, BulkRequestStore.TotalDays(runs));
    }

    [Fact]
    public void ScatteredDays_SplitIntoSeparateRuns()
    {
        var runs = BulkRequestStore.MergeRuns(new[] { D(1), D(2), D(9), D(20) });

        Assert.Equal(3, runs.Count);
        Assert.Equal((D(1), D(2)), runs[0]);
        Assert.Equal((D(9), D(9)), runs[1]);
        Assert.Equal((D(20), D(20)), runs[2]);
        Assert.Equal(4, BulkRequestStore.TotalDays(runs));
        Assert.Equal(2, BulkRequestStore.LongestRun(runs));
    }

    [Fact]
    public void DuplicateDays_CountOnce()
    {
        var runs = BulkRequestStore.MergeRuns(new[] { D(5), D(5), D(5) });

        Assert.Single(runs);
        Assert.Equal(1, BulkRequestStore.TotalDays(runs));
    }

    // ═══ الأهلية: يُستثنى غير المستحقّ مع سبب ═══

    [Fact]
    public void EligibleCandidate_HasNoRejectionReason()
    {
        var type = Type("إجازة سنوية");
        var runs = BulkRequestStore.MergeRuns(new[] { D(1), D(2) });

        Assert.Null(BulkRequestStore.RejectionReason(
            type, BulkRequestStore.ResolveEffect(type), Candidate(runs)));
    }

    [Fact]
    public void InsufficientBalance_IsRejected_NotSilentlyOverdrawn()
    {
        var type = Type("إجازة سنوية");
        var runs = BulkRequestStore.MergeRuns(new[] { D(1), D(2), D(3) });

        var reason = BulkRequestStore.RejectionReason(
            type, BulkRequestStore.ResolveEffect(type), Candidate(runs, remaining: 2m));

        Assert.NotNull(reason);
        Assert.Contains("الرصيد", reason);
    }

    [Fact]
    public void UntrackedLeaveType_IsNotBlockedByBalance()
    {
        // غير المتتبَّعة (وفاة/غير مدفوعة) بلا رصيد أصلاً — فحصُها برصيدٍ صفر كان
        // سيمنعها دائماً.
        var type = Type("إجازة وفاة");
        var runs = BulkRequestStore.MergeRuns(new[] { D(1), D(2) });

        Assert.Null(BulkRequestStore.RejectionReason(
            type, BulkRequestStore.ResolveEffect(type), Candidate(runs, remaining: 0m)));
    }

    [Fact]
    public void OverlappingRequest_IsRejected_SoBalanceIsNotDeductedTwice()
    {
        var type = Type("إجازة سنوية");
        var runs = BulkRequestStore.MergeRuns(new[] { D(1) });

        var reason = BulkRequestStore.RejectionReason(
            type, BulkRequestStore.ResolveEffect(type), Candidate(runs, overlap: true));

        Assert.NotNull(reason);
        Assert.Contains("مرّتين", reason);
    }

    [Fact]
    public void FailingEligibilityConditions_AreRejectedFirst()
    {
        var type = Type("إجازة سنوية");
        var runs = BulkRequestStore.MergeRuns(new[] { D(1) });

        var reason = BulkRequestStore.RejectionReason(
            type, BulkRequestStore.ResolveEffect(type), Candidate(runs, matches: false, overlap: true));

        Assert.Equal("لا تنطبق عليه شروط أهلية النوع", reason);
    }

    [Fact]
    public void MaxPerRequest_AppliesToLongestRun_NotTotal()
    {
        // الحدّ حدُّ الطلب الواحد: يومان متتاليان ويومان متباعدان = ثلاثة طلبات
        // أطولها يومان — فلا يُمنع بحدٍّ قدره يومان.
        var type = Type("مهمة عمل", maxPerRequest: 2);
        var effect = BulkRequestStore.ResolveEffect(type);
        var ok = BulkRequestStore.MergeRuns(new[] { D(1), D(2), D(9), D(20) });
        var tooLong = BulkRequestStore.MergeRuns(new[] { D(1), D(2), D(3) });

        Assert.Null(BulkRequestStore.RejectionReason(type, effect, Candidate(ok)));
        Assert.NotNull(BulkRequestStore.RejectionReason(type, effect, Candidate(tooLong)));
    }

    [Fact]
    public void AllowedDays_CapsTheTotalAcrossRuns()
    {
        var type = Type("مهمة عمل", allowedDays: 3);
        var effect = BulkRequestStore.ResolveEffect(type);
        var runs = BulkRequestStore.MergeRuns(new[] { D(1), D(2), D(9), D(20) });

        var reason = BulkRequestStore.RejectionReason(type, effect, Candidate(runs));

        Assert.NotNull(reason);
        Assert.Contains("المسموح", reason);
    }

    // ═══ تفكيك التحديد ═══

    [Fact]
    public void Selection_ParsesEmployeeDayPairs_AndDropsGarbage()
    {
        var targets = IndexModel.ParseSelection(new[]
        {
            "7|2026-08-01", "7|2026-08-01", "9|2026-08-02",
            "", "abc|2026-08-01", "7|not-a-date", "7", null
        });

        Assert.Equal(2, targets.Count);
        Assert.Contains((7, new DateOnly(2026, 8, 1)), targets);
        Assert.Contains((9, new DateOnly(2026, 8, 2)), targets);
    }
}
