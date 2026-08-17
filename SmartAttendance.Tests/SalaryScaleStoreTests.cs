using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>سلم الرواتب — المنطق النقي: احتواء المدى واقتراح الدرجة بالترتيب.</summary>
public class SalaryScaleStoreTests
{
    private static SalaryScaleStore.Grade G(string code, decimal? min, decimal? max, int sort = 0, bool active = true) =>
        new() { Code = code, Name = code, MinBasic = min, MaxBasic = max, SortOrder = sort, IsActive = active };

    [Fact]
    public void Contains_NullBoundsAreOpen()
    {
        Assert.True(G("A", null, null).Contains(1));
        Assert.True(G("A", 100, null).Contains(100));
        Assert.False(G("A", 100, null).Contains(99));
        Assert.True(G("A", null, 500).Contains(500));
        Assert.False(G("A", null, 500).Contains(501));
    }

    [Fact]
    public void Suggest_PicksFirstMatchingBySortOrder()
    {
        var grades = new[]
        {
            G("G3", 3000, 5000, sort: 30),
            G("G1", 0, 1500, sort: 10),
            G("G2", 1500.01m, 3000, sort: 20)
        };
        Assert.Equal("G2", SalaryScaleStore.Suggest(grades, 2000)?.Code);
        Assert.Equal("G1", SalaryScaleStore.Suggest(grades, 950)?.Code);
        Assert.Equal("G3", SalaryScaleStore.Suggest(grades, 5000)?.Code);
        Assert.Null(SalaryScaleStore.Suggest(grades, 9000));
    }

    [Fact]
    public void Suggest_IgnoresInactiveAndUnbounded()
    {
        var grades = new[]
        {
            G("X", 0, 10000, active: false),
            G("Open", null, null),          // بلا مدى ⟹ لا تُقترح (لا معنى للاقتراح)
            G("G", 900, 1000)
        };
        Assert.Equal("G", SalaryScaleStore.Suggest(grades, 950)?.Code);
        Assert.Null(SalaryScaleStore.Suggest(grades, 5000));
    }
}
