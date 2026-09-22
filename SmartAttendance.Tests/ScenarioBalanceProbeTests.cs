using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class ScenarioBalanceProbeTests
{
    [Fact]
    public async Task ScenarioRequests_ProduceExpectedBalances()
    {
        var cs = Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options);

        var snapshots = await CompanyLeavePolicyStore.GetBalanceSnapshotsAsync(
            db, 1, new DateOnly(2026, 9, 22));

        var annual = Assert.Single(snapshots, x => x.SourceRequestTypeId == 1);
        var sick = Assert.Single(snapshots, x => x.SourceRequestTypeId == 2);

        Assert.Equal(21m, annual.Entitlement);
        Assert.Equal(2m, annual.Approved);
        Assert.Equal(2m, annual.PendingReserved);
        Assert.Equal(4m, annual.Reserved);
        Assert.Equal(17m, annual.Remaining);

        Assert.Equal(30m, sick.Entitlement);
        Assert.Equal(1m, sick.Approved);
        Assert.Equal(0m, sick.PendingReserved);
        Assert.Equal(1m, sick.Reserved);
        Assert.Equal(29m, sick.Remaining);
    }

    [Fact]
    public async Task ScenarioAttendance_ExposesScheduleAndSequentialPunches()
    {
        var cs = Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options);

        var date = new DateOnly(2026, 9, 19);
        var workday = await CompanyLeavePolicyStore.GetWorkdaySnapshotAsync(db, 1, date);
        Assert.True(workday.IsWorking);
        Assert.True(workday.ScheduledHours > 0m);

        var times = await PunchTypingEngine.DayPunchTimesAsync(db, 1, date);
        Assert.Equal(2, times.Count);
        Assert.Equal(new TimeSpan(8, 42, 0), times[0].TimeOfDay);
        Assert.Equal(new TimeSpan(19, 5, 0), times[1].TimeOfDay);

        var typed = PunchTypingEngine.Derive(times);
        Assert.Equal("In", typed[0].Type);
        Assert.Equal("Out", typed[1].Type);
        Assert.Equal(623, (int)(typed[1].At - typed[0].At).TotalMinutes);
    }

    [Fact]
    public async Task ScenarioAnnualPending_StartsRealApprovalFlow()
    {
        var cs = Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options);

        var requestId = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT TOP 1 Id FROM SelfServiceRequests
WHERE EmployeeId=1 AND RequestTypeId=1 AND Status='Pending' AND CreatedBy=N'ScenarioSeed'
ORDER BY Id;
""");
        Assert.True(requestId > 0, "Annual pending preview scenario was not found.");

        var started = await ApprovalWorkflowEngine.StartAsync(db, requestId, "Leave", 1);
        Assert.True(started.Ok, started.Message);

        var flow = await ApprovalWorkflowEngine.GetFlowAsync(db, requestId);
        Assert.NotNull(flow);
        Assert.NotEmpty(flow!.Steps);
        Assert.NotEmpty(flow.CurrentSteps);
    }
}
