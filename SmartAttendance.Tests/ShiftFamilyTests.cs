using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تصنيف عائلة المناوبة (تجميع منتقي الفرشاة بالروستر): صباحية [5–12)،
/// مسائية [12–18)، ليلية [18–5)، المرنة عائلة مستقلة، والتالف «أخرى».
/// </summary>
public class ShiftFamilyTests
{
    [Theory]
    [InlineData("05:00", "صباحية")]
    [InlineData("08:00", "صباحية")]
    [InlineData("11:30", "صباحية")]
    [InlineData("12:00", "مسائية")]
    [InlineData("14:00", "مسائية")]
    [InlineData("17:59", "مسائية")]
    [InlineData("18:00", "ليلية")]
    [InlineData("23:00", "ليلية")]
    [InlineData("00:00", "ليلية")]
    [InlineData("04:59", "ليلية")]
    public void FamilyOf_ByStartHour(string start, string expected) =>
        Assert.Equal(expected, ShiftTypeStore.FamilyOf(start, isFlexible: false));

    [Fact]
    public void FamilyOf_Flexible_OwnFamily_RegardlessOfTime() =>
        Assert.Equal("مرنة", ShiftTypeStore.FamilyOf("08:00", isFlexible: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("ab:00")]
    [InlineData("25:00")]
    public void FamilyOf_Malformed_Other(string? start) =>
        Assert.Equal("أخرى", ShiftTypeStore.FamilyOf(start, isFlexible: false));
}

/// <summary>حراس «استبدال وأرشفة»: معرّفات غير صالحة أو متطابقة تُرفَض قبل أي مساس بالبيانات.</summary>
public class ReplaceArchiveGuardTests
{
    [Theory]
    [InlineData(0, 5)]    // قديمة غير محددة
    [InlineData(5, 0)]    // بديلة غير محددة
    [InlineData(-1, 5)]
    [InlineData(7, 7)]    // نفس المناوبة
    public async Task InvalidIds_NoOp(int oldId, int newId)
    {
        // db=null يثبت أن الحارس يرجع قبل أي وصول لقاعدة البيانات.
        var result = await ShiftTypeStore.ReplaceAndArchiveAsync(
            null!, oldId, newId, new DateOnly(2026, 7, 28), migrateAssignments: true);
        Assert.Equal((0, 0), result);
    }
}
