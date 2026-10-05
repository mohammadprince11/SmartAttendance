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

    [Fact]
    public void FinalApproval_QueuesDurableEffectsInsideApprovalTransaction()
    {
        var root = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "ApprovalWorkflowEngine.cs"));
        var jobs = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "ApprovalEffectJobStore.cs"));

        Assert.Contains("INSERT INTO ApprovalEffectJobs", workflow, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync()", workflow, StringComparison.Ordinal);
        Assert.Contains("ClaimPendingAsync", jobs, StringComparison.Ordinal);
        Assert.Contains("CompletedAtUtc=SYSUTCDATETIME()", jobs, StringComparison.Ordinal);
        Assert.Contains("NextAttemptAtUtc=DATEADD", jobs, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledMigration_CreatesApprovalEffectOutbox()
    {
        var migration = Assert.Single(
            SqlSchemaMigrator.Migrations,
            item => item.Id == "20260929-02-approval-effect-jobs");

        Assert.Contains("CREATE TABLE ApprovalEffectJobs", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (RequestId)", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("IX_ApprovalEffectJobs_Pending", migration.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldDecisions_AreSavedAfterScopeAndStepClaimInsideApprovalTransaction()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs"));
        var method = source[source.IndexOf("public static async Task<ActionResult> ApproveAsync(", StringComparison.Ordinal)..];
        var scopeCheck = method.IndexOf("CanAccessOwnedRowAsync", StringComparison.Ordinal);
        var transaction = method.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var claimCheck = method.IndexOf("if (claimed != 1)", StringComparison.Ordinal);
        var decisions = method.IndexOf("SetFieldDecisionsAsync", StringComparison.Ordinal);
        var commit = method.IndexOf("await transaction.CommitAsync()", StringComparison.Ordinal);
        Assert.True(scopeCheck >= 0 && scopeCheck < transaction);
        Assert.True(transaction < claimCheck && claimCheck < decisions && decisions < commit);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
