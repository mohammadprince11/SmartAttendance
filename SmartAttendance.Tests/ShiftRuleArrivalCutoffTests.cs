using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public class ShiftRuleArrivalCutoffTests
{
    private static readonly DateOnly WorkDate = new(2026, 9, 20);

    private static ShiftRuleStore.ShiftRule Rule() => new()
    {
        ConditionField = "CheckIn",
        Comparison = "After",
        ValueKind = "Time",
        ValueTime = "09:30",
        ValueAnchor = "Same",
        ActionType = "Violation",
        ActionText = "الوصول بعد 09:30 غير مسموح"
    };

    private static DayAttendanceStore.DayRow Day(string checkIn) => new()
    {
        WorkDate = WorkDate,
        DayKind = "Work",
        Status = "Present",
        CheckIn = WorkDate.ToDateTime(TimeOnly.Parse(checkIn))
    };

    [Fact]
    public void ArrivalExactlyAtCutoff_DoesNotMatchViolation()
    {
        Assert.Null(ShiftRuleStore.Evaluate(Rule(), Day("09:30"), shiftDay: null));
    }

    [Fact]
    public void ArrivalOneMinuteAfterCutoff_MatchesViolation()
    {
        var reason = ShiftRuleStore.Evaluate(Rule(), Day("09:31"), shiftDay: null);

        Assert.NotNull(reason);
        Assert.Contains("09:31", reason);
    }

    [Fact]
    public void ArrivalBeforeCutoff_DoesNotMatchViolation()
    {
        Assert.Null(ShiftRuleStore.Evaluate(Rule(), Day("09:00"), shiftDay: null));
    }
}
