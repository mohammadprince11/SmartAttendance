using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Integration tests for the Access Roles resolver against the real self-healing
/// store. Uses sentinel user/role ids, cleaned up around every test; auto-skips
/// when SQL is unreachable.
/// </summary>
public sealed class AccessRoleServiceIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private const int SentinelUser = 900500;

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await AccessRoleStore.EnsureAsync(_db);
            await CleanupAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dbAvailable)
        {
            await CleanupAsync();
        }
        await _db.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        // Remove any sentinel roles (name-tagged) and the sentinel user's assignments.
        await SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.ExecuteAsync(
            _db,
            """
DELETE FROM AccessRoleGrants WHERE RoleId IN (SELECT Id FROM AccessRoles WHERE NameAr = N'__ITEST__');
DELETE FROM UserAccessRoles WHERE SystemUserId = @u OR RoleId IN (SELECT Id FROM AccessRoles WHERE NameAr = N'__ITEST__');
DELETE FROM AccessRoles WHERE NameAr = N'__ITEST__';
""",
            command => SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.AddParameter(command, "@u", SentinelUser));
    }

    private async Task<int> CreateRoleAsync(string roleType, IEnumerable<AccessRoleStore.AccessRoleGrant> grants)
    {
        var id = await AccessRoleStore.SaveAsync(_db, new AccessRoleStore.AccessRole
        {
            RoleType = roleType,
            NameAr = "__ITEST__",
            IsActive = true,
        });
        await AccessRoleStore.ReplaceGrantsAsync(_db, id, grants);
        await AccessRoleStore.ReplaceAssignedUsersAsync(_db, id, new[] { SentinelUser });
        return id;
    }

    private static AccessRoleStore.AccessRoleGrant PageGrant(string page, params string[] actions) =>
        new() { GrantKey = page, Payload = System.Text.Json.JsonSerializer.Serialize(actions) };

    private static AccessRoleStore.AccessRoleGrant DataGrant(string entity, string scope) =>
        new() { GrantKey = entity, Payload = System.Text.Json.JsonSerializer.Serialize(new { scope }) };

    [SkippableFact]
    public async Task UserWithNoRoles_IsUnrestricted()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var profile = await new AccessRoleService(_db).ResolveAsync(SentinelUser);

        Assert.False(profile.HasPagesRole);
        Assert.True(profile.CanViewPage("People.Directory"));
        Assert.Equal("All", profile.DataScopeFor("Employees"));
    }

    [SkippableFact]
    public async Task PagesRole_RestrictsToGrantedPages()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        await CreateRoleAsync(AccessRoleStore.TypePages, new[]
        {
            PageGrant("People.Directory", "View", "Edit"),
        });

        var profile = await new AccessRoleService(_db).ResolveAsync(SentinelUser);

        Assert.True(profile.HasPagesRole);
        Assert.True(profile.Can("People.Directory", "Edit"));
        Assert.False(profile.Can("People.Directory", "Delete"));
        Assert.False(profile.CanViewPage("Payroll.TaxSocial"));
    }

    [SkippableFact]
    public async Task DataRoles_MergeToWidestScopePerEntity()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        await CreateRoleAsync(AccessRoleStore.TypeData, new[] { DataGrant("Employees", "OwnDepartment") });
        await CreateRoleAsync(AccessRoleStore.TypeData, new[] { DataGrant("Employees", "OwnBranch"), DataGrant("SalaryComponents", "Self") });

        var profile = await new AccessRoleService(_db).ResolveAsync(SentinelUser);

        Assert.Equal("OwnBranch", profile.DataScopeFor("Employees")); // wider of dept/branch
        Assert.Equal("Self", profile.DataScopeFor("SalaryComponents"));
        Assert.Equal("All", profile.DataScopeFor("Leaves"));
    }

    [SkippableFact]
    public async Task InactiveRole_IsIgnored()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var roleId = await CreateRoleAsync(AccessRoleStore.TypePages, new[] { PageGrant("People.Directory", "View") });
        await AccessRoleStore.ToggleActiveAsync(_db, roleId); // deactivate

        var profile = await new AccessRoleService(_db).ResolveAsync(SentinelUser);

        // No active Pages role → unrestricted again.
        Assert.False(profile.HasPagesRole);
        Assert.True(profile.CanViewPage("Payroll.TaxSocial"));
    }
}
