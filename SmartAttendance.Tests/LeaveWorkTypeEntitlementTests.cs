using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LeaveWorkTypeEntitlementTests
{
    [Theory]
    [InlineData("دوام جزئي")]
    [InlineData("Part Time")]
    [InlineData("Part-Time")]
    [InlineData("PT")]
    public void PartTimeWorkType_IsRecognized(string workType)
    {
        Assert.True(CompanyLeavePolicyStore.IsPartTimeWorkType(workType));
    }

    [Theory]
    [InlineData("دوام كامل")]
    [InlineData("Full Time")]
    [InlineData("Shifts")]
    [InlineData(null)]
    public void NonPartTimeWorkType_IsNotReduced(string? workType)
    {
        Assert.False(CompanyLeavePolicyStore.IsPartTimeWorkType(workType));
    }

    [Fact]
    public void AnnualLeave_PartTime_GetsHalfEntitlement()
    {
        var policy = new CompanyLeavePolicyStore.Policy
        {
            EffectCode = RequestTypeEffectCatalog.LeaveAnnual,
            EntitlementAmount = 21m
        };

        Assert.Equal(10.5m,
            CompanyLeavePolicyStore.EffectiveEntitlementForWorkType(policy, "دوام جزئي"));
        Assert.Equal(21m,
            CompanyLeavePolicyStore.EffectiveEntitlementForWorkType(policy, "دوام كامل"));
    }

    [Fact]
    public void SickLeave_RemainsFixedForPartTime()
    {
        var policy = new CompanyLeavePolicyStore.Policy
        {
            EffectCode = RequestTypeEffectCatalog.LeaveSick,
            EntitlementAmount = 30m
        };

        Assert.Equal(30m,
            CompanyLeavePolicyStore.EffectiveEntitlementForWorkType(policy, "دوام جزئي"));
    }
}
