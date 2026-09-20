using System.IO;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LeaveWorkingScheduleTests
{
    [Theory]
    [InlineData("Work", false, true)]
    [InlineData("Remote", false, true)]
    [InlineData("BusinessTrip", false, true)]
    [InlineData("Rest", false, false)]
    [InlineData("Weekend", false, false)]
    [InlineData("Work", true, false)]
    public void ChargeableLeaveDay_FollowsAttendanceWorkingKinds(
        string dayKind,
        bool isHoliday,
        bool expected)
    {
        Assert.Equal(expected, CompanyLeavePolicyStore.IsChargeableLeaveDay(dayKind, isHoliday));
    }

    [Fact]
    public void ScheduleResolver_UsesCompanyRosterOverridesAndHolidays()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms", "CompanyLeavePolicyStore.cs"));

        Assert.Contains("ShiftTypeStore.ListInScopeAsync", source);
        Assert.Contains("ShiftOverrideStore.MapAsync", source);
        Assert.Contains("RosterStore.MapAsync", source);
        Assert.Contains("FROM Holidays", source);
        Assert.Contains("EmployeeMatchesEligibility", source);
        Assert.DoesNotContain("if(unit==\"Days\")return toDate.DayNumber-fromDate.DayNumber+1", source);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
