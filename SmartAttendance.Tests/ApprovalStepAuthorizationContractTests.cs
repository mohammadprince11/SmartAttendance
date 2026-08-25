using Xunit;

namespace SmartAttendance.Tests;

/// <summary>يثبت أن نطاق الشركة وحده لا يكفي للبتّ؛ يجب أن يطابق الفاعل الخطوة الحالية.</summary>
public sealed class ApprovalStepAuthorizationContractTests
{
    [Theory]
    [InlineData("إجازة سنوية", "LeaveRequest")]
    [InlineData("إجازة مرضية", "LeaveRequest")]
    [InlineData("مغادرة شخصية", "ExitPermission")]
    [InlineData("خروج عمل", "ExitPermission")]
    [InlineData("نسيان بصمة", "MissingPunch")]
    [InlineData("عمل إضافي", "Overtime")]
    [InlineData("تعديل البيانات", "InfoChange")]
    [InlineData("قرض", "Loan")]
    public void DynamicRequestNames_ResolveToConfiguredTemplateKeys(string requestType, string expected) =>
        Assert.Equal(expected,
            SmartAttendance.Web.Infrastructure.Hrms.ApprovalWorkflowEngine.ResolveRequestTypeKey(requestType));

    [Fact]
    public void ApproveAndReject_EnforceCurrentStepActorInsideEngine()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs"));

        Assert.Equal(2, Count(source, "!CanAct(current, actor, actorRoles"));
        Assert.Contains("e.DirectManagerId = @ActorEmployeeId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DataChangeFieldDecisions_AreWrittenOnlyAfterAuthorizedApproval()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml.cs"));
        var handler = source.IndexOf("OnPostApproveAsync", StringComparison.Ordinal);
        var approval = source.IndexOf("ApprovalWorkflowEngine.ApproveAsync", handler, StringComparison.Ordinal);
        var decisions = source.IndexOf("SetFieldDecisionsAsync", handler, StringComparison.Ordinal);

        Assert.True(handler >= 0 && approval > handler && decisions > approval);
        Assert.Contains("if (result.Ok)", source[approval..decisions], StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }
}
