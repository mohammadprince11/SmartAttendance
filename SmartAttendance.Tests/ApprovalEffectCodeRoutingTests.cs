using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalEffectCodeRoutingTests
{
    [Theory]
    [InlineData("LeaveAnnual", "LeaveRequest")]
    [InlineData("LeaveSick", "LeaveRequest")]
    [InlineData("LeaveUnpaid", "LeaveRequest")]
    [InlineData("LeaveOther", "LeaveRequest")]
    [InlineData("BusinessTrip", "LeaveRequest")]
    [InlineData("ExitPermission", "ExitPermission")]
    [InlineData("Overtime", "Overtime")]
    [InlineData("WorkFromHome", "WorkFromHome")]
    [InlineData("ShiftChange", "ShiftChange")]
    public void EffectCode_MapsToStableApprovalTemplateKey(string effectCode, string expected) =>
        Assert.Equal(expected, ApprovalWorkflowEngine.ResolveRequestTypeKeyFromEffectCode(effectCode));

    [Theory]
    [InlineData("")]
    [InlineData("UnknownEffect")]
    public void UnknownEffectCode_DoesNotInventApprovalRoute(string effectCode) =>
        Assert.Null(ApprovalWorkflowEngine.ResolveRequestTypeKeyFromEffectCode(effectCode));
}
