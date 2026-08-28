using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PayrollSeparationOfDutiesTests
{
    [Fact]
    public void PayrollApproval_RejectsTheCalculatorIdentity()
    {
        var source = Read("SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs");
        Assert.Contains("run.CalculatedBy.Equals(approver", source, StringComparison.Ordinal);
        Assert.Contains("فصل الواجبات", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BankExport_RequiresIssuedRunAndUsesOwningCompany()
    {
        var source = Read("SmartAttendance.Web", "Pages", "Payroll", "RunDetail.cshtml.cs");
        Assert.Contains("run.Status is not (\"Issued\" or \"PayslipSent\")", source, StringComparison.Ordinal);
        Assert.Contains("WHERE Id=@Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY Id", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
