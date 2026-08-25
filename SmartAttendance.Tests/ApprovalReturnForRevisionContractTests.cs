using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalReturnForRevisionContractTests
{
    [Fact]
    public void ReturnAndResubmit_RequireScopeOwnershipAndPreserveFrozenWorkflow()
    {
        var root = FindRoot();
        var engine = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs"));
        var approvals = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml.cs"));
        var portal = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "EmployeePortal", "Index.cshtml.cs"));

        var returnStart = engine.IndexOf("ReturnForRevisionAsync(", StringComparison.Ordinal);
        var resubmitStart = engine.IndexOf("ResubmitReturnedAsync(", StringComparison.Ordinal);
        Assert.True(returnStart >= 0 && resubmitStart > returnStart);
        var returnSource = engine[returnStart..resubmitStart];
        var resubmitEnd = engine.IndexOf("ResolveRequestTypeKey(", resubmitStart, StringComparison.Ordinal);
        var resubmitSource = engine[resubmitStart..resubmitEnd];

        Assert.Contains("string.IsNullOrWhiteSpace(note)", returnSource, StringComparison.Ordinal);
        Assert.Contains("EmployeeCompanyGuard.CanAccessOwnedRowAsync", returnSource, StringComparison.Ordinal);
        Assert.Contains("IsRequesterAsync(dbContext, requestId, actor, actorEmployeeId)", returnSource, StringComparison.Ordinal);
        Assert.Contains("r.Status='Pending'", returnSource, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", returnSource, StringComparison.Ordinal);

        Assert.Contains("r.EmployeeId=@EmployeeId", resubmitSource, StringComparison.Ordinal);
        Assert.Contains("r.Status='Returned'", resubmitSource, StringComparison.Ordinal);
        Assert.Contains("s.Status='Returned'", resubmitSource, StringComparison.Ordinal);
        Assert.Contains("Status='Current'", resubmitSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StartAsync", resubmitSource, StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.ReturnForRevisionAsync", approvals, StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.ResubmitReturnedAsync", portal, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
