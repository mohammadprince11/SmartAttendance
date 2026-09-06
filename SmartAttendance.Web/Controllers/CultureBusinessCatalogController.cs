using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers;

[ApiController]
[Authorize]
[IgnoreAntiforgeryToken]
[Route("Culture/BusinessCatalog")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class CultureBusinessCatalogController : ControllerBase
{
    private const int MaxValues = 500;
    private const int MaxValueLength = 1_000;

    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public CultureBusinessCatalogController(
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [HttpPost]
    public async Task<IActionResult> PostAsync(
        [FromBody] BusinessCatalogRequest? request,
        CancellationToken cancellationToken)
    {
        var pageValues = (request?.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value =>
                value.Length <= MaxValueLength &&
                ContainsArabicScript(value))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxValues)
            .ToArray();

        if (pageValues.Length == 0)
        {
            return Ok(new
            {
                aliases =
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
            });
        }

        var scope =
            await _companyScope.GetAsync(
                cancellationToken);

        if (scope.IsDeniedAll)
        {
            return Ok(new
            {
                aliases =
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
            });
        }

        var allowedCompanyIds =
            scope.AllowedCompanyIds.ToArray();

        bool AppearsOnPage(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return pageValues.Any(value =>
                value.Contains(
                    source,
                    StringComparison.Ordinal));
        }

        var candidates =
            new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);

        void Offer(
            string? source,
            string? translated)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(translated) ||
                string.Equals(
                    source,
                    translated,
                    StringComparison.Ordinal) ||
                !ContainsArabicScript(source) ||
                !AppearsOnPage(source))
            {
                return;
            }

            if (!candidates.TryGetValue(
                    source,
                    out var targets))
            {
                targets = new HashSet<string>(
                    StringComparer.Ordinal);

                candidates[source] = targets;
            }

            targets.Add(translated);
        }

        var companyQuery =
            _db.Companies
                .AsNoTracking()
                .Where(company =>
                    !company.IsDeleted);

        if (!scope.IsUnrestricted)
        {
            companyQuery = companyQuery.Where(
                company =>
                    allowedCompanyIds.Contains(
                        company.Id));
        }

        var companies =
            await companyQuery
                .Select(company => new
                {
                    company.Id,
                    company.Name
                })
                .ToListAsync(cancellationToken);

        var localizedCompanies =
            await EmployeeBusinessDataDisplayLocalizer
                .GetCompanyNamesAsync(
                    _db,
                    companies.Select(item =>
                        (item.Id, item.Name)),
                    cancellationToken);

        foreach (var company in companies)
        {
            if (localizedCompanies.TryGetValue(
                    company.Id,
                    out var translated))
            {
                Offer(
                    company.Name,
                    translated);
            }
        }

        var branchQuery =
            _db.Branches
                .AsNoTracking()
                .Where(branch =>
                    !branch.IsDeleted);

        if (!scope.IsUnrestricted)
        {
            branchQuery = branchQuery.Where(
                branch =>
                    allowedCompanyIds.Contains(
                        branch.CompanyId));
        }

        var branches =
            await branchQuery
                .Select(branch => new
                {
                    branch.Id,
                    branch.CompanyId,
                    branch.Name
                })
                .ToListAsync(cancellationToken);

        var localizedBranches =
            await EmployeeBusinessDataDisplayLocalizer
                .GetEntityNamesAsync(
                    _db,
                    "Branch",
                    branches.Select(item => (
                        item.CompanyId,
                        EntityId: item.Id,
                        Fallback: item.Name)),
                    cancellationToken);

        foreach (var branch in branches)
        {
            if (localizedBranches.TryGetValue(
                    (branch.CompanyId, branch.Id),
                    out var translated))
            {
                Offer(
                    branch.Name,
                    translated);
            }
        }

        var departmentQuery =
            _db.Departments
                .AsNoTracking()
                .Where(department =>
                    !department.IsDeleted);

        if (!scope.IsUnrestricted)
        {
            departmentQuery =
                departmentQuery.Where(
                    department =>
                        allowedCompanyIds.Contains(
                            department.CompanyId));
        }

        var departments =
            await departmentQuery
                .Select(department => new
                {
                    department.Id,
                    department.CompanyId,
                    department.Name
                })
                .ToListAsync(cancellationToken);

        var localizedDepartments =
            await EmployeeBusinessDataDisplayLocalizer
                .GetEntityNamesAsync(
                    _db,
                    "Department",
                    departments.Select(item => (
                        item.CompanyId,
                        EntityId: item.Id,
                        Fallback: item.Name)),
                    cancellationToken);

        foreach (var department in departments)
        {
            if (localizedDepartments.TryGetValue(
                    (
                        department.CompanyId,
                        department.Id),
                    out var translated))
            {
                Offer(
                    department.Name,
                    translated);
            }
        }

        var positionQuery =
            _db.HrJobPositions
                .AsNoTracking();

        if (!scope.IsUnrestricted)
        {
            positionQuery =
                positionQuery.Where(position =>
                    allowedCompanyIds.Contains(
                        position.CompanyId));
        }

        var positions =
            await positionQuery
                .Select(position => new
                {
                    position.Id,
                    position.CompanyId,
                    Name = position.ArabicName
                })
                .ToListAsync(cancellationToken);

        var localizedPositions =
            await EmployeeBusinessDataDisplayLocalizer
                .GetEntityNamesAsync(
                    _db,
                    "Position",
                    positions.Select(item => (
                        item.CompanyId,
                        EntityId: item.Id,
                        Fallback: item.Name)),
                    cancellationToken);

        foreach (var position in positions)
        {
            if (localizedPositions.TryGetValue(
                    (
                        position.CompanyId,
                        position.Id),
                    out var translated))
            {
                Offer(
                    position.Name,
                    translated);
            }
        }

        var exactPageValues =
            pageValues.ToHashSet(
                StringComparer.Ordinal);

        var employeeQuery =
            _db.Employees
                .AsNoTracking()
                .Where(employee =>
                    !employee.IsDeleted &&
                    (
                        exactPageValues.Contains(
                            employee.FullName) ||
                        (
                            employee.Position != null &&
                            exactPageValues.Contains(
                                employee.Position)
                        )
                    ));

        if (!scope.IsUnrestricted)
        {
            employeeQuery =
                employeeQuery.Where(employee =>
                    allowedCompanyIds.Contains(
                        employee.CompanyId ??
                        employee.Branch.CompanyId));
        }

        var employees =
            await employeeQuery
                .Select(employee => new
                {
                    employee.Id,
                    employee.FullName,
                    employee.Position
                })
                .ToListAsync(cancellationToken);

        var localizedEmployees =
            await EmployeeBusinessDataDisplayLocalizer
                .GetEmployeeBusinessDataAsync(
                    _db,
                    employees.Select(item =>
                        item.Id),
                    cancellationToken);

        foreach (var employee in employees)
        {
            if (!localizedEmployees.TryGetValue(
                    employee.Id,
                    out var translated))
            {
                continue;
            }

            Offer(
                employee.FullName,
                translated.FullName);

            Offer(
                employee.Position,
                translated.Position);
        }

        // Same legacy source can theoretically exist in two companies with
        // different target translations. Never guess in that case.
        var aliases =
            candidates
                .Where(pair =>
                    pair.Value.Count == 1)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Single(),
                    StringComparer.Ordinal);

        return Ok(new
        {
            aliases
        });
    }

    private static bool ContainsArabicScript(
        string value)
    {
        return value.Any(character =>
            character >= '\u0600' &&
            character <= '\u06ff');
    }

    public sealed class BusinessCatalogRequest
    {
        public List<string> Values { get; set; } = [];
    }
}