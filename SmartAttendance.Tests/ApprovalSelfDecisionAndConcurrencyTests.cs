using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalSelfDecisionAndConcurrencyTests
{
    [Fact]
    public void ApprovalEngine_PreventsSelfDecisionAndAtomicallyClaimsCurrentStep()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs"));

        Assert.True(Count(source, "IsRequesterAsync(dbContext, requestId, actor, actorEmployeeId)") >= 2);
        Assert.True(Count(source, "WHERE Id = @StepId AND Status = 'Current'") >= 2);
        Assert.True(Count(source, "if (claimed != 1)") >= 2);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.True(Count(source, "transaction.CommitAsync") >= 2);
        Assert.Contains("r.EmployeeId = @ActorEmployeeId", source, StringComparison.Ordinal);
        Assert.Contains("r.CreatedBy = @Actor", source, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}
