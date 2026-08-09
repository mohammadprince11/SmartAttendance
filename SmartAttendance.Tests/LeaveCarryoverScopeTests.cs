using SmartAttendance.Domain.Enums;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

public class LeaveCarryoverScopeTests
{
    [Fact]
    public async Task Carry_over_rejects_company_outside_scope_before_database_access()
    {
        var scope = CompanyScope.ForCompanies(new[] { 11 });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LeaveCarryoverService.CarryOverAsync(
                null!,
                scope,
                companyId: 22,
                fromYear: 2026,
                types: new[] { LeaveType.Annual },
                cap: null,
                userName: "test"));
    }

    [Fact]
    public void Leave_balances_page_uses_effective_company_scope()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "LeaveBalances",
            "Index.cshtml.cs"));

        Assert.Contains("ICompanyScopeProvider", source, StringComparison.Ordinal);
        Assert.Contains("scope.Allows(CompanyId.Value)", source, StringComparison.Ordinal);
        Assert.Contains("allowed.Contains(x.Id)", source, StringComparison.Ordinal);
    }
}
