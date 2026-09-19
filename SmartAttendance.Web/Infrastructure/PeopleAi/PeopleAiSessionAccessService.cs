using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.PeopleAi;

public sealed record PeopleAiAccessContext(
    int? SystemUserId,
    bool IsAdmin,
    PeopleDataScope Scope,
    HashSet<int> AllowedCompanyIds);

public interface IPeopleAiSessionAccessService
{
    Task<PeopleAiAccessContext> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    bool CanAccessSession(
        EmployeeOnboardingStore.SessionRow? session,
        int companyId,
        PeopleAiAccessContext access);
}

public sealed class PeopleAiSessionAccessService :
    IPeopleAiSessionAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly IEffectiveScopeService _effectiveScopeService;

    public PeopleAiSessionAccessService(
        ApplicationDbContext db,
        IEffectiveScopeService effectiveScopeService)
    {
        _db = db;
        _effectiveScopeService = effectiveScopeService;
    }

    public async Task<PeopleAiAccessContext> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var role = PeopleAccessContext.GetRole(httpContext);
        var isAdmin = RoleRouteCatalog.IsAdmin(role);
        var systemUserId =
            PeopleAccessContext.GetSystemUserId(httpContext);

        var scope = await _effectiveScopeService
            .GetEmployeesAccessScopeAsync(
                systemUserId ?? 0,
                isAdmin,
                cancellationToken);

        HashSet<int> companyIds;
        if (scope.IsUnrestricted && !scope.IsDeniedAll)
        {
            companyIds = await _db.Companies
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive)
                .Select(x => x.Id)
                .ToHashSetAsync(cancellationToken);
        }
        else
        {
            companyIds = scope.AllowedCompanyIds.ToHashSet();

            var scopedCompanyIds = await _db.Employees
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .ApplyPeopleDataScope(scope)
                .Select(x => x.Branch.CompanyId)
                .Distinct()
                .ToListAsync(cancellationToken);

            companyIds.UnionWith(scopedCompanyIds);
            companyIds.ExceptWith(scope.DeniedCompanyIds);
        }

        return new PeopleAiAccessContext(
            systemUserId,
            isAdmin,
            scope,
            companyIds);
    }

    public bool CanAccessSession(
        EmployeeOnboardingStore.SessionRow? session,
        int companyId,
        PeopleAiAccessContext access)
    {
        if (session is null ||
            session.CompanyId != companyId ||
            !access.AllowedCompanyIds.Contains(companyId))
        {
            return false;
        }

        if (access.IsAdmin)
        {
            return true;
        }

        return access.SystemUserId is > 0 &&
               session.CreatedBySystemUserId ==
               access.SystemUserId;
    }
}
