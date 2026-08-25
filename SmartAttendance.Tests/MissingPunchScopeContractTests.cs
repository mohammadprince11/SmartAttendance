using Xunit;

namespace SmartAttendance.Tests;

public sealed class MissingPunchScopeContractTests
{
    [Fact]
    public void ListAndSave_ApplyScopeAndFiltersBeforeMaterialization()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "MissingPunchRequestStore.cs"));

        Assert.Contains("r.EmployeeId = @EmployeeId", source, StringComparison.Ordinal);
        Assert.Contains("r.Status = @Status", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeCompanyGuard.CanAccessEmployeeAsync(db, r.EmployeeId, scope)", source, StringComparison.Ordinal);
        Assert.Contains("expectedEmployeeId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rows = rows.Where", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackOfficeEmployeePicker_IsCompanyScoped()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "MissingPunchRequests", "Index.cshtml.cs"));

        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope", source, StringComparison.Ordinal);
        Assert.Contains("authorizationScope: scope", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_AtomicallyClaimsPendingRequestBeforeCreatingPunch()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "MissingPunchRequestStore.cs"));

        var claim = source.IndexOf("SET Status=N'Processing'", StringComparison.Ordinal);
        var insert = source.IndexOf("INSERT INTO AttendanceRecords", StringComparison.Ordinal);
        Assert.True(claim >= 0 && insert > claim);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("WHERE Id=@Id AND Status=N'Processing'", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
    }
}
