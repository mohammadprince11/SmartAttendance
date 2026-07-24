using System.Collections.Generic;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات مطابقة الشرائح التصاعدية <see cref="PeriodRuleStore.MatchSlice"/> — دالة
/// نقية: الشريحة المطابِقة هي [SliceFrom ≤ value &lt; SliceTo)، والأعلى تفوز، والأخيرة
/// المفتوحة (SliceTo=null) تلتقط ما فوق. مثال العقوبة المتدرّجة.
/// </summary>
public class PeriodRuleSliceTests
{
    private static List<PeriodRuleStore.Slice> Slices() => new()
    {
        new() { SliceFrom = 0,  SliceTo = 10, ActionText = "إنذار" },
        new() { SliceFrom = 10, SliceTo = 20, ActionText = "خصم يوم" },
        new() { SliceFrom = 20, SliceTo = null, ActionText = "خصم يومين" }, // ما فوق
    };

    [Theory]
    [InlineData(0, "إنذار")]      // الحدّ الأدنى ضمن الأولى
    [InlineData(5, "إنذار")]
    [InlineData(9.99, "إنذار")]
    [InlineData(10, "خصم يوم")]   // الحدّ يفتح الشريحة التالية (شامل من، حصري إلى)
    [InlineData(15, "خصم يوم")]
    [InlineData(20, "خصم يومين")] // الشريحة المفتوحة
    [InlineData(100, "خصم يومين")]
    public void MatchSlice_PicksCorrectTier(double value, string expectedAction)
    {
        var slice = PeriodRuleStore.MatchSlice(Slices(), (decimal)value);
        Assert.NotNull(slice);
        Assert.Equal(expectedAction, slice!.ActionText);
    }

    [Fact]
    public void MatchSlice_BelowFirst_ReturnsNull()
    {
        var slices = new List<PeriodRuleStore.Slice>
        {
            new() { SliceFrom = 5, SliceTo = 10, ActionText = "x" },
        };
        Assert.Null(PeriodRuleStore.MatchSlice(slices, 3m)); // أقل من أدنى حدّ
    }

    [Fact]
    public void MatchSlice_Empty_ReturnsNull() =>
        Assert.Null(PeriodRuleStore.MatchSlice(new List<PeriodRuleStore.Slice>(), 50m));
}
