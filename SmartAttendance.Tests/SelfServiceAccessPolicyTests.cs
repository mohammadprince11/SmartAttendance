using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class SelfServiceAccessPolicyTests
{
    [Theory]
    [InlineData("إجازة سنوية", "LeaveRequest")]
    [InlineData("Leave", "LeaveRequest")]
    [InlineData("مغادرة شخصية", "ExitPermission")]
    [InlineData("خروج عمل", "ExitPermission")]
    [InlineData("نسيان بصمة", "PunchCorrection")]
    [InlineData("Punch correction", "PunchCorrection")]
    [InlineData("أوفر تايم", "OvertimeRequest")]
    [InlineData("عمل إضافي", "OvertimeRequest")]
    public void RequestTypes_MapToServerSideAction(string requestType, string expected) =>
        Assert.Equal(expected, SelfServiceAccessPolicy.ActionForRequestType(requestType));

    [Theory]
    [InlineData("LeaveAnnual", "LeaveRequest")]
    [InlineData("LeaveSick", "LeaveRequest")]
    [InlineData("LeaveUnpaid", "LeaveRequest")]
    [InlineData("BusinessTrip", "LeaveRequest")]
    [InlineData("WorkFromHome", "LeaveRequest")]
    [InlineData("ExitPermission", "ExitPermission")]
    [InlineData("Overtime", "OvertimeRequest")]
    [InlineData("ShiftChange", "ShiftRequest")]
    public void EffectCodes_MapToStableServerSideAction(string effectCode, string expected) =>
        Assert.Equal(expected, SelfServiceAccessPolicy.ActionForEffectCode(effectCode));

    [Theory]
    [InlineData("")]
    [InlineData("UnknownEffect")]
    public void UnknownEffectCode_FailsClosed(string effectCode) =>
        Assert.Null(SelfServiceAccessPolicy.ActionForEffectCode(effectCode));

    [Theory]
    [InlineData("")]
    [InlineData("نوع مخصص غير معروف")]
    public void UnknownRequestType_FailsClosed(string requestType) =>
        Assert.Null(SelfServiceAccessPolicy.ActionForRequestType(requestType));
}
