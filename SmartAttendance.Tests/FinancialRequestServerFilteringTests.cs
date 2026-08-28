using Xunit;

namespace SmartAttendance.Tests;

public sealed class FinancialRequestServerFilteringTests
{
    [Fact]
    public void ListingAndPortalOwnership_AreEnforcedInsideSql()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "FinancialRequestStore.cs"));

        Assert.Contains("r.EmployeeId = @EmployeeId", source, StringComparison.Ordinal);
        Assert.Contains("f.Kind = @Kind", source, StringComparison.Ordinal);
        Assert.Contains("r.Status = @Status", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeId=@e", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rows = rows.Where", source, StringComparison.Ordinal);
    }
}
