using Xunit;

namespace SmartAttendance.Tests;

public sealed class LeaveRequestServerScopeTests
{
    [Fact]
    public void LeaveListsAndPickers_ApplyScopeBeforeExecutingQuery()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Infrastructure", "Services", "LeaveRequestService.cs"));

        Assert.Contains("LeaveRequests.Query().AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("Employees.Query().AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("allowedCompanies.Contains", source, StringComparison.Ordinal);
        Assert.Contains("var scopedRows = await query.ToListAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaveRequests.GetAllAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Employees.GetAllAsync()", source, StringComparison.Ordinal);
    }
}
