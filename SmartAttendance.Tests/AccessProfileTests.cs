using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>Pure tests for the Access Roles resolution rules.</summary>
public class AccessProfileTests
{
    private static AccessProfile Build(
        bool hasPagesRole,
        (string, IEnumerable<string>)[]? pages = null,
        (string, string)[]? data = null) =>
        AccessProfile.Build(
            hasPagesRole,
            pages ?? System.Array.Empty<(string, IEnumerable<string>)>(),
            data ?? System.Array.Empty<(string, string)>());

    [Fact]
    public void NoPagesRole_IsUnrestricted()
    {
        var profile = Build(hasPagesRole: false);
        Assert.True(profile.CanViewPage("People.Directory"));
        Assert.True(profile.Can("People.Directory", "Delete"));
    }

    [Fact]
    public void WithPagesRole_OnlyGrantedPagesAreVisible()
    {
        var profile = Build(true, pages: new[]
        {
            ("People.Directory", (IEnumerable<string>)new[] { "View", "Edit" }),
        });

        Assert.True(profile.CanViewPage("People.Directory"));
        Assert.True(profile.Can("People.Directory", "Edit"));
        Assert.False(profile.Can("People.Directory", "Delete"));
        Assert.False(profile.CanViewPage("Payroll.TaxSocial"));
    }

    [Fact]
    public void PageGranted_WithoutViewAction_IsNotViewable()
    {
        var profile = Build(true, pages: new[]
        {
            ("People.Reports", (IEnumerable<string>)new[] { "Export" }),
        });

        Assert.False(profile.CanViewPage("People.Reports"));
    }

    [Fact]
    public void PageActions_AreUnionedAcrossRoles()
    {
        // Same page granted by two roles with different actions.
        var profile = Build(true, pages: new[]
        {
            ("People.Directory", (IEnumerable<string>)new[] { "View" }),
            ("People.Directory", (IEnumerable<string>)new[] { "Edit" }),
        });

        Assert.True(profile.Can("People.Directory", "View"));
        Assert.True(profile.Can("People.Directory", "Edit"));
        Assert.False(profile.Can("People.Directory", "Delete"));
    }

    [Fact]
    public void NoDataRole_ScopeIsAll()
    {
        var profile = Build(false);
        Assert.Equal("All", profile.DataScopeFor("Employees"));
    }

    [Theory]
    [InlineData("All", "OwnBranch", "All")]        // All is widest
    [InlineData("Self", "OwnDepartment", "OwnDepartment")]
    [InlineData("OwnBranch", "OwnCompany", "OwnCompany")]
    [InlineData("Self", "Self", "Self")]
    public void DataScope_WidestWinsAcrossRoles(string a, string b, string expected)
    {
        var profile = Build(false, data: new[]
        {
            ("Employees", a),
            ("Employees", b),
        });

        Assert.Equal(expected, profile.DataScopeFor("Employees"));
    }

    [Fact]
    public void DataScope_IsPerEntity()
    {
        var profile = Build(false, data: new[]
        {
            ("Employees", "OwnBranch"),
            ("SalaryComponents", "Self"),
        });

        Assert.Equal("OwnBranch", profile.DataScopeFor("Employees"));
        Assert.Equal("Self", profile.DataScopeFor("SalaryComponents"));
        Assert.Equal("All", profile.DataScopeFor("Leaves")); // ungranted → unrestricted
    }
}
