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

    [Fact]
    public void PolicyOverride_ExactAllowance_CreatesNoAbsence()
    {
        var days = new[]
        {
            Day(1, 1, 0.50m),
            Day(1, 2, 0.50m),
            Day(1, 3, 1.00m)
        };

        var rows = AttendancePolicyOverrideStore.BuildLateAllowanceOverrides(
            days,
            new AttendanceLatenessPolicy.Policy(true, 120, true));

        Assert.Empty(rows);
    }

    [Fact]
    public void PolicyOverride_FirstDayCrossingAllowance_IsAbsent()
    {
        var days = new[]
        {
            Day(7, 1, 1.00m),
            Day(7, 2, 1.00m),
            Day(7, 3, 0.25m),
            Day(7, 4, 0.10m)
        };

        var rows = AttendancePolicyOverrideStore.BuildLateAllowanceOverrides(
            days,
            new AttendanceLatenessPolicy.Policy(true, 120, true));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 9, 3), rows[0].WorkDate);
        Assert.Equal(new DateOnly(2026, 9, 4), rows[1].WorkDate);
        Assert.All(rows, row => Assert.Equal("Absent", row.OverrideStatus));
    }

    [Fact]
    public void PolicyOverride_Disabled_CreatesNothing()
    {
        var rows = AttendancePolicyOverrideStore.BuildLateAllowanceOverrides(
            new[] { Day(1, 1, 2m), Day(1, 2, 1m) },
            new AttendanceLatenessPolicy.Policy(false, 120, true));

        Assert.Empty(rows);
    }

    [Fact]
    public void PolicyOverride_DoesNotMixEmployees()
    {
        var days = new[]
        {
            Day(1, 1, 1.50m),
            Day(1, 2, 1.00m),
            Day(2, 1, 0.50m),
            Day(2, 2, 0.50m)
        };

        var rows = AttendancePolicyOverrideStore.BuildLateAllowanceOverrides(
            days,
            new AttendanceLatenessPolicy.Policy(true, 120, true));

        var row = Assert.Single(rows);
        Assert.Equal(1, row.EmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 2), row.WorkDate);
    }

    private static DayAttendanceStore.DayRow Day(int employeeId, int day, decimal lateHours) => new()
    {
        EmployeeId = employeeId,
        WorkDate = new DateOnly(2026, 9, day),
        DayKind = "Work",
        Status = "Late",
        LateHours = lateHours
    };
}
