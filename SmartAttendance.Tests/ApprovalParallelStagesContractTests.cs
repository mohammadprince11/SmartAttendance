using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalParallelStagesContractTests
{
    [Fact]
    public void ParallelStages_ArePersistedActivatedTogetherAndSerialized()
    {
        var root=FindRoot();
        var template=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalTemplateStore.cs"));
        var engine=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        var page=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Pages","HrSettings","ApprovalTemplates.cshtml"));

        Assert.Contains("20260826-13-approval-parallel-stages",migration,StringComparison.Ordinal);
        Assert.Contains("StageOrder",template,StringComparison.Ordinal);
        Assert.Contains("StageOrder",page,StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(System.Data.IsolationLevel.Serializable)",engine,StringComparison.Ordinal);
        Assert.True(Count(engine,"AcquireDecisionLockAsync(dbContext,requestId)")>=3);
        Assert.Contains("step.StageOrder==current.StageOrder",engine,StringComparison.Ordinal);
        Assert.Contains("WHERE RequestId=@Id AND StageOrder=@NextStage AND Status='Pending'",engine,StringComparison.Ordinal);
        Assert.Contains("Status IN ('Returned','WaitingRevision')",engine,StringComparison.Ordinal);
    }

    private static int Count(string source,string value)
    {
        var count=0;
        for(var index=0;(index=source.IndexOf(value,index,StringComparison.Ordinal))>=0;index+=value.Length) count++;
        return count;
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
