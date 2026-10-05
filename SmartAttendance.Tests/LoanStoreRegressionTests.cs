using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LoanStoreRegressionTests
{
    [Theory]
    [InlineData(LoanStore.Pending, LoanStore.Approved)]
    [InlineData(LoanStore.Pending, LoanStore.Rejected)]
    [InlineData(LoanStore.Approved, LoanStore.Closed)]
    [InlineData(LoanStore.Approved, LoanStore.Approved)]
    public void Allowed_transitions_are_explicit_and_idempotent(string current, string target)
    {
        Assert.True(LoanStore.CanTransition(current, target));
    }

    [Theory]
    [InlineData(LoanStore.Approved, LoanStore.Pending)]
    [InlineData(LoanStore.Rejected, LoanStore.Approved)]
    [InlineData(LoanStore.Closed, LoanStore.Approved)]
    [InlineData(LoanStore.Pending, LoanStore.Closed)]
    [InlineData(LoanStore.Pending, "Anything")]
    public void Invalid_transitions_are_rejected(string current, string target)
    {
        Assert.False(LoanStore.CanTransition(current, target));
    }

    [Fact]
    public void Save_path_is_pending_only_atomic_and_does_not_trust_form_status()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms", "LoanStore.cs"));
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Payroll", "Loans.cshtml.cs"));
        var view = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Payroll", "Loans.cshtml"));
        var migration = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));

        Assert.Contains("loan.Status = Pending;", store, StringComparison.Ordinal);
        Assert.Contains("WHERE Id=@Id AND Status = N'Pending'", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Status IN (N'Pending', N'Approved')", store, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.CurrentTransaction is null", store, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", store, StringComparison.Ordinal);
        Assert.Contains("await RegenerateScheduleAsync(dbContext, loanId, loan);", store, StringComparison.Ordinal);
        Assert.Contains("COALESCE(@AttName, AttachmentName)", store, StringComparison.Ordinal);
        Assert.Contains("WHERE l.RequestKey = @RequestKey", store, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE EmployeeLoans ADD RequestKey", store, StringComparison.Ordinal);
        Assert.DoesNotContain("UX_EmployeeLoans_RequestKey", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Status = form[\"Status\"]", page, StringComparison.Ordinal);
        Assert.Contains("RequestKey = NullIfEmpty(form[\"RequestKey\"])", page, StringComparison.Ordinal);
        Assert.Contains("id=\"f_RequestKey\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"f_Status\"", view, StringComparison.Ordinal);
        Assert.Contains("20260920-09-payroll-loan-idempotency", migration, StringComparison.Ordinal);
        Assert.Contains("UX_EmployeeLoans_RequestKey", migration, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
