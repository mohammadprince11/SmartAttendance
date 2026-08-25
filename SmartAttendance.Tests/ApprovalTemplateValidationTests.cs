using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemplateValidationTests
{
    private static ApprovalTemplateStore.TemplateRow Valid() => new()
    {
        RequestType = "LeaveRequest",
        Name = "اعتماد الإجازة",
        Steps = { new ApprovalTemplateStore.StepRow { ApproverType = "DirectManager", DisplayName = "المدير" } }
    };

    [Fact]
    public void ValidTemplate_IsAccepted() => Assert.Null(ApprovalTemplateStore.Validate(Valid()));

    [Fact]
    public void EmptyCommittee_IsRejected()
    {
        var template = Valid();
        template.Steps.Clear();
        Assert.NotNull(ApprovalTemplateStore.Validate(template));
    }

    [Theory]
    [InlineData("Role")]
    [InlineData("User")]
    public void UnresolvedApprover_IsRejected(string type)
    {
        var template = Valid();
        template.Steps[0].ApproverType = type;
        Assert.NotNull(ApprovalTemplateStore.Validate(template));
    }

    [Fact]
    public void UnknownRequestType_IsRejected()
    {
        var template = Valid();
        template.RequestType = "ForgedType";
        Assert.NotNull(ApprovalTemplateStore.Validate(template));
    }
}
