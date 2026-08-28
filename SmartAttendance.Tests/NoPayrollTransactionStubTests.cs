using Xunit;

namespace SmartAttendance.Tests;

public sealed class NoPayrollTransactionStubTests
{
    [Fact]
    public void TransactionsPage_HasNoUpcomingOrDisabledStubFilters()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var page = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "Payroll", "Transactions.cshtml"));
        var model = File.ReadAllText(Path.Combine(
            directory.FullName, "SmartAttendance.Web", "Pages", "Payroll", "Transactions.cshtml.cs"));

        Assert.DoesNotContain("قريباً", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowUpcomingFilters", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowUpcomingFilters", model, StringComparison.Ordinal);
    }
}
