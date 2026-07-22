using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Produces the Access Roles contribution to the effective People data scope,
/// expressed as a <see cref="PeopleDataScope"/> so callers can AND it with the
/// existing rules-based scope. Admin, a missing user, or an "All" Data scope all
/// resolve to Unrestricted — so this can only ever tighten, never widen.
/// </summary>
public interface IEffectiveScopeService
{
    Task<PeopleDataScope> GetEmployeesAccessScopeAsync(
        int systemUserId, bool isAdmin, CancellationToken cancellationToken = default);
}

public sealed class EffectiveScopeService : IEffectiveScopeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAccessRoleService _accessRoleService;

    public EffectiveScopeService(ApplicationDbContext dbContext, IAccessRoleService accessRoleService)
    {
        _dbContext = dbContext;
        _accessRoleService = accessRoleService;
    }

    public async Task<PeopleDataScope> GetEmployeesAccessScopeAsync(
        int systemUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (isAdmin || systemUserId <= 0)
        {
            return PeopleDataScope.Unrestricted();
        }

        var profile = await _accessRoleService.ResolveAsync(systemUserId, cancellationToken);
        var scope = profile.DataScopeFor("Employees");

        // Fast path: no narrowing Data role → no anchor lookup needed.
        if (string.Equals(scope, "All", StringComparison.OrdinalIgnoreCase))
        {
            return PeopleDataScope.Unrestricted();
        }

        var anchors = await ResolveAnchorsAsync(systemUserId, cancellationToken);
        return AccessRoleScopeTranslator.ToPeopleDataScope(scope, anchors);
    }

    private async Task<AccessRoleScopeTranslator.UserAnchors> ResolveAnchorsAsync(
        int systemUserId, CancellationToken cancellationToken)
    {
        var employeeId = await _dbContext.SystemUsers.AsNoTracking()
            .Where(u => u.Id == systemUserId)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (employeeId is not > 0)
        {
            return new AccessRoleScopeTranslator.UserAnchors(0, 0, 0, null);
        }

        var anchors = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && !e.IsDeleted)
            .Select(e => new AccessRoleScopeTranslator.UserAnchors(
                e.Branch.CompanyId, e.BranchId, e.DepartmentId, e.Id))
            .FirstOrDefaultAsync(cancellationToken);

        return anchors.EmployeeId is > 0
            ? anchors
            : new AccessRoleScopeTranslator.UserAnchors(0, 0, 0, employeeId);
    }
}
