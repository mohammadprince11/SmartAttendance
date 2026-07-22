using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Tests that Access Roles scopes translate into a PeopleDataScope with the
/// right allow behaviour (verified through the existing AllowsEmployee check).
/// </summary>
public class AccessRoleScopeTranslatorTests
{
    // Acting user: company 1, branch 5, department 12, employee 100.
    private static readonly AccessRoleScopeTranslator.UserAnchors Anchors = new(CompanyId: 1, BranchId: 5, DepartmentId: 12, EmployeeId: 100);

    [Theory]
    [InlineData("All")]
    [InlineData("None")]
    [InlineData(null)]
    [InlineData("Weird")]
    public void WideOrUnknownScopes_AllowEveryEmployee(string? scope)
    {
        var s = AccessRoleScopeTranslator.ToPeopleDataScope(scope, Anchors);
        Assert.True(s.AllowsEmployee(employeeId: 999, companyId: 9, branchId: 9, departmentId: 9));
    }

    [Fact]
    public void OwnBranch_AllowsSameBranchOnly()
    {
        var s = AccessRoleScopeTranslator.ToPeopleDataScope("OwnBranch", Anchors);
        Assert.True(s.AllowsEmployee(200, 1, 5, 30));   // same branch
        Assert.False(s.AllowsEmployee(201, 1, 6, 12));  // different branch
    }

    [Fact]
    public void OwnDepartment_AllowsSameDepartmentOnly()
    {
        var s = AccessRoleScopeTranslator.ToPeopleDataScope("OwnDepartment", Anchors);
        Assert.True(s.AllowsEmployee(200, 1, 9, 12));
        Assert.False(s.AllowsEmployee(201, 1, 5, 13));
    }

    [Fact]
    public void OwnCompany_AllowsSameCompanyOnly()
    {
        var s = AccessRoleScopeTranslator.ToPeopleDataScope("OwnCompany", Anchors);
        Assert.True(s.AllowsEmployee(200, 1, 88, 88));
        Assert.False(s.AllowsEmployee(201, 2, 5, 12));
    }

    [Fact]
    public void Self_AllowsOnlyTheActingUser()
    {
        var s = AccessRoleScopeTranslator.ToPeopleDataScope("Self", Anchors);
        Assert.True(s.AllowsEmployee(100, 1, 5, 12));
        Assert.False(s.AllowsEmployee(101, 1, 5, 12));
    }

    [Fact]
    public void NarrowScope_WithoutAnchor_FallsBackToUnrestricted()
    {
        // A user with no org anchor must not be silently denied everything.
        var noAnchor = new AccessRoleScopeTranslator.UserAnchors(0, 0, 0, null);
        var s = AccessRoleScopeTranslator.ToPeopleDataScope("OwnBranch", noAnchor);
        Assert.True(s.AllowsEmployee(1, 1, 1, 1));
    }
}
