using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalSlaContractTests
{
    [Fact]
    public void Sla_RemindsOnceEscalatesPerStepAndAuthorizesTheAlternate()
    {
        var root=FindRoot();
        var engine=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        var program=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Program.cs"));
        var worker=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalSlaDispatcherService.cs"));
        Assert.Contains("20260826-14-approval-sla-reminders-alternates",migration,StringComparison.Ordinal);
        Assert.Contains("s.ReminderSentAt IS NULL",engine,StringComparison.Ordinal);
        Assert.Contains("s.EscalatedAt IS NULL",engine,StringComparison.Ordinal);
        Assert.Contains("EscalatedToUser",engine,StringComparison.Ordinal);
        Assert.Contains("step.EscalatedAt is not null",engine,StringComparison.Ordinal);
        Assert.Contains("ApprovalSlaDispatcherService",program,StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer(TimeSpan.FromMinutes(5))",worker,StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
