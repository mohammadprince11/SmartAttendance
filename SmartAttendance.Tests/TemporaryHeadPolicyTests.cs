using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات رئيس الوحدة المؤقت.
///
/// أخطرها <see cref="ActingHead_DoesNotBecomeOwnManager"/>: لو ناب الرئيس المؤقت
/// عن نفسه لصار موظفٌ مديرَ نفسه — فتُعتمد طلباته ذاتياً وتضيع الرقابة كلّها.
///
/// و<see cref="ExpiredAllocation_RestoresOriginalManager"/> يثبّت أن النيابة
/// **طبقةٌ تنتهي بتاريخها** لا استبدالاً دائماً للمدير المباشر.
/// </summary>
public class TemporaryHeadPolicyTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static TemporaryHeadPolicy.Allocation Alloc(
        int id, int dept, int head, string from, string to, bool active = true) =>
        new(id, dept, head, DateOnly.Parse(from), DateOnly.Parse(to), active);

    [Fact]
    public void NoAllocation_KeepsOriginalManager()
    {
        var result = TemporaryHeadPolicy.EffectiveManagerId(
            employeeId: 100, departmentId: 3, originalManagerId: 7,
            Array.Empty<TemporaryHeadPolicy.Allocation>(), Today);

        Assert.Equal(7, result);
    }

    [Fact]
    public void EffectiveAllocation_ReplacesManagerForThatUnitOnly()
    {
        var allocations = new[] { Alloc(1, dept: 3, head: 55, "2026-08-10", "2026-08-20") };

        Assert.Equal(55, TemporaryHeadPolicy.EffectiveManagerId(100, 3, 7, allocations, Today));
        // وحدة أخرى لا تتأثر إطلاقاً.
        Assert.Equal(7, TemporaryHeadPolicy.EffectiveManagerId(100, 4, 7, allocations, Today));
    }

    [Fact]
    public void ExpiredAllocation_RestoresOriginalManager()
    {
        var allocations = new[] { Alloc(1, 3, 55, "2026-07-01", "2026-07-31") };

        Assert.Equal(7, TemporaryHeadPolicy.EffectiveManagerId(100, 3, 7, allocations, Today));
    }

    [Fact]
    public void ScheduledFutureAllocation_DoesNotApplyYet()
    {
        var allocations = new[] { Alloc(1, 3, 55, "2026-09-01", "2026-09-10") };

        Assert.Equal(7, TemporaryHeadPolicy.EffectiveManagerId(100, 3, 7, allocations, Today));
    }

    [Fact]
    public void InactiveAllocation_IsIgnored()
    {
        var allocations = new[] { Alloc(1, 3, 55, "2026-08-10", "2026-08-20", active: false) };

        Assert.Equal(7, TemporaryHeadPolicy.EffectiveManagerId(100, 3, 7, allocations, Today));
    }

    [Fact]
    public void BoundsAreInclusive()
    {
        var allocation = Alloc(1, 3, 55, "2026-08-15", "2026-08-15");

        Assert.True(TemporaryHeadPolicy.IsEffective(allocation, Today));
        Assert.False(TemporaryHeadPolicy.IsEffective(allocation, Today.AddDays(-1)));
        Assert.False(TemporaryHeadPolicy.IsEffective(allocation, Today.AddDays(1)));
    }

    [Fact]
    public void NewestOverlappingAllocationWins()
    {
        var allocations = new[]
        {
            Alloc(1, 3, 55, "2026-08-01", "2026-08-31"),
            Alloc(9, 3, 77, "2026-08-10", "2026-08-20")
        };

        // إسنادان متداخلان = تصحيح لاحق، والأحدث هو المقصود.
        Assert.Equal(77, TemporaryHeadPolicy.EffectiveManagerId(100, 3, 7, allocations, Today));
    }

    [Fact]
    public void ActingHead_DoesNotBecomeOwnManager()
    {
        var allocations = new[] { Alloc(1, 3, head: 55, "2026-08-10", "2026-08-20") };

        // الموظف 55 هو الرئيس المؤقت نفسه — يبقى مديره الأصلي.
        Assert.Equal(7, TemporaryHeadPolicy.EffectiveManagerId(55, 3, 7, allocations, Today));
    }

    [Fact]
    public void InvertedRange_IsRejected()
    {
        Assert.False(TemporaryHeadPolicy.IsValidRange(new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 10)));
        Assert.True(TemporaryHeadPolicy.IsValidRange(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public void Overlapping_DetectsCrossingRangesInSameUnitOnly()
    {
        var existing = new[]
        {
            Alloc(1, 3, 55, "2026-08-01", "2026-08-12"),
            Alloc(2, 4, 66, "2026-08-01", "2026-08-31"),
            Alloc(3, 3, 77, "2026-09-01", "2026-09-30")
        };

        var candidate = Alloc(99, 3, 88, "2026-08-10", "2026-08-20");
        var overlaps = TemporaryHeadPolicy.Overlapping(candidate, existing);

        Assert.Single(overlaps);
        Assert.Equal(1, overlaps[0].Id);
    }

    [Fact]
    public void StatusLabel_DistinguishesScheduledFromExpired()
    {
        Assert.Equal("سارٍ الآن", TemporaryHeadPolicy.StatusLabel(Alloc(1, 3, 55, "2026-08-10", "2026-08-20"), Today));
        Assert.Equal("مجدول", TemporaryHeadPolicy.StatusLabel(Alloc(1, 3, 55, "2026-09-01", "2026-09-10"), Today));
        Assert.Equal("منتهٍ", TemporaryHeadPolicy.StatusLabel(Alloc(1, 3, 55, "2026-07-01", "2026-07-31"), Today));
        Assert.Equal("معطّل", TemporaryHeadPolicy.StatusLabel(Alloc(1, 3, 55, "2026-08-10", "2026-08-20", false), Today));
    }
}
