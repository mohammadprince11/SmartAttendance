using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>Pure tests for the Employees data-scope evaluator.</summary>
public class EmployeeScopeEvaluatorTests
{
    // Acting user: branch 5, department 12, employee 100.
    private static readonly EmployeeScopeEvaluator.ActingUser User = new(BranchId: 5, DepartmentId: 12, EmployeeId: 100);

    private static EmployeeScopeEvaluator.EmployeeRef Emp(int id, int branch, int dept) => new(id, branch, dept);

    [Theory]
    [InlineData("All")]
    [InlineData("OwnCompany")]
    [InlineData(null)]
    [InlineData("Weird")]
    public void WideOrUnknownScopes_AdmitEveryEmployee(string? scope)
    {
        Assert.True(EmployeeScopeEvaluator.IsInScope(scope, User, Emp(999, 88, 88)));
    }

    [Fact]
    public void OwnBranch_MatchesOnlySameBranch()
    {
        Assert.True(EmployeeScopeEvaluator.IsInScope("OwnBranch", User, Emp(200, 5, 30)));
        Assert.False(EmployeeScopeEvaluator.IsInScope("OwnBranch", User, Emp(201, 6, 12)));
    }

    [Fact]
    public void OwnDepartment_MatchesOnlySameDepartment()
    {
        Assert.True(EmployeeScopeEvaluator.IsInScope("OwnDepartment", User, Emp(200, 9, 12)));
        Assert.False(EmployeeScopeEvaluator.IsInScope("OwnDepartment", User, Emp(201, 5, 13)));
    }

    [Fact]
    public void Self_MatchesOnlyTheActingUsersRecord()
    {
        Assert.True(EmployeeScopeEvaluator.IsInScope("Self", User, Emp(100, 5, 12)));
        Assert.False(EmployeeScopeEvaluator.IsInScope("Self", User, Emp(101, 5, 12)));
    }

    [Fact]
    public void NarrowScopes_DenyWhenActingUserAnchorIsUnknown()
    {
        // A user with no branch/department/employee context is never in a narrow scope.
        var unknown = new EmployeeScopeEvaluator.ActingUser(0, 0, 0);
        Assert.False(EmployeeScopeEvaluator.IsInScope("OwnBranch", unknown, Emp(1, 0, 0)));
        Assert.False(EmployeeScopeEvaluator.IsInScope("OwnDepartment", unknown, Emp(1, 0, 0)));
        Assert.False(EmployeeScopeEvaluator.IsInScope("Self", unknown, Emp(1, 0, 0)));
    }
}
