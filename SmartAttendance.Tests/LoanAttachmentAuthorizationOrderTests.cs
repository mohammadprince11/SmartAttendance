using Xunit;

namespace SmartAttendance.Tests;

public sealed class LoanAttachmentAuthorizationOrderTests
{
    [Fact]
    public void EmployeeOwnership_IsCheckedBeforeProtectedAttachmentIsWritten()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "Payroll", "Loans.cshtml.cs"));

        var guard = source.IndexOf("EmployeeCompanyGuard.CanAccessEmployeeAsync", StringComparison.Ordinal);
        var write = source.IndexOf("_protectedFiles.SaveAsync", StringComparison.Ordinal);
        Assert.True(guard >= 0 && write > guard, "The company-scope guard must run before writing a loan attachment.");
    }
}
