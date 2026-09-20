using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public class AttendanceLateAllowancePolicyTests
{
    private static AttendanceLateAllowancePolicy.LateDay Late(
        int day,
        int minutes) =>
        new(new DateOnly(2026, 9, day), minutes);

    [Fact]
    public void ExactAllowance_DoesNotCreateViolation()
    {
        var rows = AttendanceLateAllowancePolicy.Evaluate(
            new[] { Late(1, 30), Late(3, 40), Late(7, 50) },
            allowanceMinutes: 120);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsViolationDay));
        Assert.Equal(0, rows[^1].AllowanceRemaining);
        Assert.Equal(0, rows.Sum(row => row.ExcessMinutes));
    }

    [Fact]
    public void FirstLateDayAfterAllowance_IsViolation()
    {
        var rows = AttendanceLateAllowancePolicy.Evaluate(
            new[] { Late(1, 60), Late(2, 60), Late(3, 10) },
            allowanceMinutes: 120);

        Assert.False(rows[0].IsViolationDay);
        Assert.False(rows[1].IsViolationDay);
        Assert.True(rows[2].IsViolationDay);
        Assert.Equal(10, rows[2].ExcessMinutes);
        Assert.Equal(0, rows[2].AllowanceRemaining);
    }

    [Fact]
    public void DayThatCrossesAllowance_IsViolationForItsExcess()
    {
        var rows = AttendanceLateAllowancePolicy.Evaluate(
            new[] { Late(1, 100), Late(2, 30) },
            allowanceMinutes: 120);

        Assert.False(rows[0].IsViolationDay);
        Assert.True(rows[1].IsViolationDay);
        Assert.Equal(20, rows[1].CoveredMinutes);
        Assert.Equal(10, rows[1].ExcessMinutes);
    }

    [Fact]
    public void DuplicateRowsOnSameDate_AreAggregatedBeforeAllowance()
    {
        var rows = AttendanceLateAllowancePolicy.Evaluate(
            new[] { Late(5, 70), Late(5, 60), Late(6, 5) },
            allowanceMinutes: 120);

        Assert.Equal(2, rows.Count);
        Assert.Equal(130, rows[0].LateMinutes);
        Assert.Equal(10, rows[0].ExcessMinutes);
        Assert.True(rows[0].IsViolationDay);
        Assert.True(rows[1].IsViolationDay);
    }

    [Fact]
    public void NonPositiveLateMinutes_AreIgnored()
    {
        var rows = AttendanceLateAllowancePolicy.Evaluate(
            new[] { Late(1, 0), Late(2, -5), Late(3, 15) },
            allowanceMinutes: 120);

        Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 9, 3), rows[0].Date);
        Assert.Equal(105, rows[0].AllowanceRemaining);
    }

    [Fact]
    public void NegativeAllowance_IsTreatedAsZero()
    {
        var row = Assert.Single(
            AttendanceLateAllowancePolicy.Evaluate(
                new[] { Late(1, 1) },
                allowanceMinutes: -10));
        Assert.True(row.IsViolationDay);
        Assert.Equal(1, row.ExcessMinutes);
        Assert.Equal(0, row.AllowanceRemaining);
    }
}
