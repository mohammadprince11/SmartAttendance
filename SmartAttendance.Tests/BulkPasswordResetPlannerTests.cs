using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// خطّة إعادة التعيين الجماعيّة: الحارس الوحيد هنا «لا تمسّ حسابك الشخصيّ» —
/// كلّ حالةٍ تثبت أنّ الأدمن مُستثنى ولو حدّد نفسه، مع تنظيف المكرّر والصفر.
/// </summary>
public class BulkPasswordResetPlannerTests
{
    [Fact]
    public void ExcludesCurrentAdminEvenIfSelected()
    {
        var plan = BulkPasswordResetPlanner.Build(new[] { 5, 7, 9 }, currentLoginId: 7);

        Assert.Equal(new[] { 5, 9 }, plan.Apply);
        Assert.Equal(new[] { 7 }, plan.SkippedSelf);
    }

    [Fact]
    public void KeepsAllWhenSelfNotSelected()
    {
        var plan = BulkPasswordResetPlanner.Build(new[] { 5, 9 }, currentLoginId: 7);

        Assert.Equal(new[] { 5, 9 }, plan.Apply);
        Assert.Empty(plan.SkippedSelf);
    }

    [Fact]
    public void DeduplicatesAndDropsNonPositiveIds()
    {
        var plan = BulkPasswordResetPlanner.Build(new[] { 5, 5, 0, -3, 9 }, currentLoginId: 1);

        Assert.Equal(new[] { 5, 9 }, plan.Apply);
    }

    [Fact]
    public void OnlySelfSelected_ProducesEmptyApply()
    {
        var plan = BulkPasswordResetPlanner.Build(new[] { 7 }, currentLoginId: 7);

        Assert.Empty(plan.Apply);
        Assert.Equal(new[] { 7 }, plan.SkippedSelf);
    }

    [Fact]
    public void NullOrEmptySelection_ProducesEmptyPlan()
    {
        var plan = BulkPasswordResetPlanner.Build(null, currentLoginId: 7);

        Assert.Empty(plan.Apply);
        Assert.Empty(plan.SkippedSelf);
    }
}
