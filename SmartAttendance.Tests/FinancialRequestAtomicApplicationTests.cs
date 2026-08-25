using Xunit;

namespace SmartAttendance.Tests;

public sealed class FinancialRequestAtomicApplicationTests
{
    [Fact]
    public void FinancialEffect_IsClaimedOnceInsideTransactionAfterFinalApproval()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "FinancialRequestStore.cs"));

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanAccessOwnedRowAsync", source, StringComparison.Ordinal);
        Assert.Contains("d.Applied = 0 AND r.Status = N'Approved'", source, StringComparison.Ordinal);
        Assert.Contains("if (claimed != 1) return false", source, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanyScope.Unrestricted()", source, StringComparison.Ordinal);
    }
}
