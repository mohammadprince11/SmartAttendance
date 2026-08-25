namespace SmartAttendance.Tests;

/// <summary>
/// Guards the payroll transaction list against regressing to "fetch the month and
/// filter in memory". Payroll rows and employee attributes are sensitive and the
/// repository rules require filtering in SQL together with company scope.
/// </summary>
public sealed class PayrollTransactionServerFilteringTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Store_AppliesCompanyAndAdvancedFiltersInsideSql()
    {
        var source = Read("SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollTransactionStore.cs");

        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", source);
        Assert.Contains("t.EmployeeId = @EmployeeId", source);
        Assert.Contains("d.Name = @Department", source);
        Assert.Contains("b.Name = @Branch", source);
        Assert.Contains("t.TransactionDate >= @DateFrom", source);
        Assert.Contains("t.Amount >= @MinAmount", source);
        Assert.Contains("HrmsDatabase.AddParameter(command, \"@Search\"", source);
    }

    [Fact]
    public void Page_DoesNotPostFilterPayrollRowsInMemory()
    {
        var source = Read("SmartAttendance.Web", "Pages", "Payroll", "Transactions.cshtml.cs");

        Assert.DoesNotContain("Items = Items.Where", source, StringComparison.Ordinal);
        Assert.Contains("employeeId: Emp", source);
        Assert.Contains("minAmount: MinAmount", source);
    }
}
