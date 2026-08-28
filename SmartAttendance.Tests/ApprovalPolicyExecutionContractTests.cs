using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalPolicyExecutionContractTests
{
    [Fact]
    public void Visible_approval_policies_are_executed_and_snapshotted()
    {
        var root=FindRoot();
        var engine=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        var portal=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Pages","EmployeePortal","Index.cshtml"));
        var portalModel=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Pages","EmployeePortal","Index.cshtml.cs"));

        Assert.Contains("20260826-16-approval-policy-snapshots",migration,StringComparison.Ordinal);
        Assert.Contains("ApprovalRequestWatchers",engine,StringComparison.Ordinal);
        Assert.Contains("HasRequestAttachmentAsync",engine,StringComparison.Ordinal);
        Assert.Contains("AutoRejectUnknownCommittee",engine,StringComparison.Ordinal);
        Assert.Contains("CancelByRequesterAsync",engine,StringComparison.Ordinal);
        Assert.Contains("CancelLimitDays",engine,StringComparison.Ordinal);
        Assert.Contains("DispatchConfiguredNotificationsAsync",engine,StringComparison.Ordinal);
        Assert.Contains("ShouldNotify",engine,StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"CancelRequest\"",portal,StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.CancelByRequesterAsync",portalModel,StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
