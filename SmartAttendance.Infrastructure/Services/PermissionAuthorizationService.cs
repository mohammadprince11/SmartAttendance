using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public sealed class PermissionAuthorizationService : IPermissionAuthorizationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly object _activeRulesCacheLock = new();
    private readonly Dictionary<int, Task<List<ActivePermissionRule>>>
        _activeRulesByUser = new();

    public PermissionAuthorizationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> HasDirectGrantAsync(
        int systemUserId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        return HasPermissionAsync(
            systemUserId,
            permissionCode,
            compatibilityAllowed: false,
            cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default)
    {
        var rules = await LoadActiveRulesAsync(
            systemUserId,
            permissionCode,
            cancellationToken);

        if (rules.Any(rule =>
                rule.Effect == PermissionEffect.Deny &&
                rule.ScopeType == PeopleDataScopeType.All))
        {
            return false;
        }

        if (rules.Any(IsEffectiveAllowance))
        {
            return true;
        }

        return compatibilityAllowed;
    }

    public async Task<bool> HasGlobalPermissionAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default)
    {
        var rules = await LoadActiveRulesAsync(
            systemUserId,
            permissionCode,
            cancellationToken);

        // Global actions cannot safely honour a partial data denial because there is
        // no target employee against which that denial can be evaluated.
        if (rules.Any(rule => rule.Effect == PermissionEffect.Deny))
        {
            return false;
        }

        if (rules.Any(rule =>
                rule.Effect == PermissionEffect.Allow &&
                rule.ScopeType == PeopleDataScopeType.All))
        {
            return true;
        }

        return compatibilityAllowed;
    }

    public async Task<PeopleDataScope> GetPeopleDataScopeAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityUnrestricted = false,
        CancellationToken cancellationToken = default)
    {
        var rules = await LoadActiveRulesAsync(
            systemUserId,
            permissionCode,
            cancellationToken);

        var selfEmployeeId = rules
            .Select(rule => rule.SelfEmployeeId)
            .FirstOrDefault(value => value.HasValue);

        var allowRules = rules
            .Where(rule =>
                rule.Effect == PermissionEffect.Allow &&
                IsApplicableScope(rule))
            .ToList();

        var denyRules = rules
            .Where(rule =>
                rule.Effect == PermissionEffect.Deny &&
                IsApplicableScope(rule))
            .ToList();

        return new PeopleDataScope
        {
            IsUnrestricted = compatibilityUnrestricted ||
                             allowRules.Any(rule =>
                                 rule.ScopeType == PeopleDataScopeType.All),
            IsDeniedAll = denyRules.Any(rule =>
                rule.ScopeType == PeopleDataScopeType.All),
            SelfEmployeeId = selfEmployeeId,
            AllowSelf = allowRules.Any(rule =>
                rule.ScopeType == PeopleDataScopeType.Self),
            DenySelf = denyRules.Any(rule =>
                rule.ScopeType == PeopleDataScopeType.Self),
            AllowedCompanyIds = SelectScopeIds(
                allowRules,
                PeopleDataScopeType.Company),
            AllowedBranchIds = SelectScopeIds(
                allowRules,
                PeopleDataScopeType.Branch),
            AllowedDepartmentIds = SelectScopeIds(
                allowRules,
                PeopleDataScopeType.Department),
            AllowedEmployeeIds = SelectScopeIds(
                allowRules,
                PeopleDataScopeType.Employee),
            DeniedCompanyIds = SelectScopeIds(
                denyRules,
                PeopleDataScopeType.Company),
            DeniedBranchIds = SelectScopeIds(
                denyRules,
                PeopleDataScopeType.Branch),
            DeniedDepartmentIds = SelectScopeIds(
                denyRules,
                PeopleDataScopeType.Department),
            DeniedEmployeeIds = SelectScopeIds(
                denyRules,
                PeopleDataScopeType.Employee)
        };
    }

    public async Task<bool> CanAccessEmployeeAsync(
        int systemUserId,
        string permissionCode,
        int employeeId,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            return false;
        }

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId && !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.BranchId,
                x.DepartmentId,
                CompanyId = x.Branch.CompanyId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (employee == null)
        {
            return false;
        }

        var scope = await GetPeopleDataScopeAsync(
            systemUserId,
            permissionCode,
            compatibilityAllowed,
            cancellationToken);

        return scope.AllowsEmployee(
            employee.Id,
            employee.CompanyId,
            employee.BranchId,
            employee.DepartmentId);
    }

    private async Task<List<ActivePermissionRule>> LoadActiveRulesAsync(
        int systemUserId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (systemUserId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
        {
            return new List<ActivePermissionRule>();
        }

        var normalizedCode = permissionCode.Trim();
        var allRules = await LoadActiveRulesForUserAsync(
            systemUserId,
            cancellationToken);

        return allRules
            .Where(rule => string.Equals(
                rule.PermissionCode,
                normalizedCode,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private Task<List<ActivePermissionRule>> LoadActiveRulesForUserAsync(
        int systemUserId,
        CancellationToken cancellationToken)
    {
        lock (_activeRulesCacheLock)
        {
            if (_activeRulesByUser.TryGetValue(
                    systemUserId,
                    out var cachedTask))
            {
                return cachedTask;
            }

            var loadTask = QueryActiveRulesForUserAsync(
                systemUserId,
                cancellationToken);

            _activeRulesByUser[systemUserId] = loadTask;
            return loadTask;
        }
    }

    private async Task<List<ActivePermissionRule>>
        QueryActiveRulesForUserAsync(
            int systemUserId,
            CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        return await _dbContext.SystemUserPermissions
            .AsNoTracking()
            .Where(assignment =>
                assignment.SystemUserId == systemUserId &&
                !assignment.IsDeleted &&
                assignment.SystemUser.IsActive &&
                !assignment.SystemUser.IsDeleted &&
                assignment.Permission.IsActive &&
                !assignment.Permission.IsDeleted &&
                (!assignment.ValidFromUtc.HasValue ||
                 assignment.ValidFromUtc.Value <= utcNow) &&
                (!assignment.ValidToUtc.HasValue ||
                 assignment.ValidToUtc.Value > utcNow))
            .Select(assignment => new ActivePermissionRule
            {
                PermissionCode = assignment.Permission.Code,
                Effect = assignment.Effect,
                ScopeType = assignment.ScopeType,
                ScopeCompanyId = assignment.ScopeCompanyId,
                ScopeBranchId = assignment.ScopeBranchId,
                ScopeDepartmentId = assignment.ScopeDepartmentId,
                ScopeEmployeeId = assignment.ScopeEmployeeId,
                SelfEmployeeId = assignment.SystemUser.EmployeeId
            })
            .ToListAsync(cancellationToken);
    }

    private static bool IsEffectiveAllowance(ActivePermissionRule rule)
    {
        return rule.Effect == PermissionEffect.Allow &&
               IsApplicableScope(rule);
    }

    private static bool IsApplicableScope(ActivePermissionRule rule)
    {
        return rule.ScopeType != PeopleDataScopeType.Self ||
               rule.SelfEmployeeId.HasValue;
    }

    private static int[] SelectScopeIds(
        IEnumerable<ActivePermissionRule> rules,
        PeopleDataScopeType scopeType)
    {
        return rules
            .Where(rule => rule.ScopeType == scopeType)
            .Select(rule => scopeType switch
            {
                PeopleDataScopeType.Company => rule.ScopeCompanyId,
                PeopleDataScopeType.Branch => rule.ScopeBranchId,
                PeopleDataScopeType.Department => rule.ScopeDepartmentId,
                PeopleDataScopeType.Employee => rule.ScopeEmployeeId,
                _ => null
            })
            .Where(value => value.HasValue && value.Value > 0)
            .Select(value => value!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    private sealed class ActivePermissionRule
    {
        public string PermissionCode { get; init; } = string.Empty;

        public PermissionEffect Effect { get; init; }

        public PeopleDataScopeType ScopeType { get; init; }

        public int? ScopeCompanyId { get; init; }

        public int? ScopeBranchId { get; init; }

        public int? ScopeDepartmentId { get; init; }

        public int? ScopeEmployeeId { get; init; }

        public int? SelfEmployeeId { get; init; }
    }
}
