using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class IndexModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ICompanyService _companyService;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private readonly SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService _effectiveScopeService;

    public IndexModel(
        IEmployeeService employeeService,
        ICompanyService companyService,
        IPermissionAuthorizationService permissionAuthorizationService,
        SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService effectiveScopeService)
    {
        _employeeService = employeeService;
        _companyService = companyService;
        _permissionAuthorizationService = permissionAuthorizationService;
        _effectiveScopeService = effectiveScopeService;
    }

    public List<EmployeeListViewModel> Employees { get; set; } = new();

    public List<CompanyListViewModel> CompanyOptions { get; set; } = new();

    public List<BranchListViewModel> BranchOptions { get; set; } = new();

    public List<DepartmentListViewModel> DepartmentOptions { get; set; } = new();

    public bool CanCreateEmployee { get; set; }

    public bool CanImportEmployees { get; set; }

    public string SelectedCompanyName =>
        CompanyId.HasValue
            ? CompanyOptions.FirstOrDefault(x =>
                  x.Id == CompanyId.Value)?.Name ?? "-"
            : "-";

    public int TotalEmployees { get; set; }

    public int FilteredEmployees { get; set; }

    public int ActiveEmployees { get; set; }

    public int InactiveEmployees { get; set; }

    public int NewThisYear { get; set; }

    public int TotalPages { get; set; }

    public int FirstItemNumber =>
        FilteredEmployees == 0
            ? 0
            : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber =>
        FilteredEmployees == 0
            ? 0
            : Math.Min(PageNumber * PageSize, FilteredEmployees);

    public IReadOnlyList<int?> PaginationPages =>
        BuildPaginationPages(PageNumber, TotalPages);

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? BranchId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "name";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public async Task OnGetAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        var directoryScope = await _permissionAuthorizationService
            .GetPeopleDataScopeAsync(
                systemUserId,
                PeoplePermissionCodes.ViewDirectory,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.ViewDirectory),
                HttpContext.RequestAborted);

        var accessibleCompanyIds = directoryScope.IsDeniedAll
            ? Array.Empty<int>()
            : (await _employeeService.GetAccessibleCompanyIdsAsync(directoryScope))
                .Concat(directoryScope.AllowedCompanyIds)
                .Except(directoryScope.DeniedCompanyIds)
                .Distinct()
                .ToArray();

        var allCompanies = (await _companyService.GetAllAsync())
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ToList();

        CompanyOptions = directoryScope.IsUnrestricted &&
                         !directoryScope.HasAnyDenial
            ? allCompanies
            : allCompanies
                .Where(x => accessibleCompanyIds.Contains(x.Id))
                .ToList();

        CanCreateEmployee = await _permissionAuthorizationService
            .HasGlobalPermissionAsync(
                systemUserId,
                PeoplePermissionCodes.Create,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.Create),
                HttpContext.RequestAborted);

        CanImportEmployees = await _permissionAuthorizationService
            .HasGlobalPermissionAsync(
                systemUserId,
                PeoplePermissionCodes.Import,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.Import),
                HttpContext.RequestAborted);

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        SearchTerm = string.IsNullOrWhiteSpace(SearchTerm)
            ? null
            : SearchTerm.Trim();
        StatusFilter = NormalizeStatusFilter(StatusFilter);
        SortBy = NormalizeSortBy(SortBy);
        PageNumber = Math.Max(PageNumber, 1);
        PageSize = NormalizePageSize(PageSize);

        if (!CompanyId.HasValue)
        {
            Employees = new List<EmployeeListViewModel>();
            BranchOptions = new List<BranchListViewModel>();
            DepartmentOptions = new List<DepartmentListViewModel>();
            return;
        }

        BranchOptions = (await _employeeService
                .GetBranchesForDropdownAsync(
                    CompanyId.Value,
                    directoryScope))
            .OrderBy(x => x.Name)
            .ToList();

        DepartmentOptions = (await _employeeService
                .GetDepartmentsForDropdownAsync(
                    CompanyId.Value,
                    directoryScope))
            .OrderBy(x => x.Name)
            .ToList();

        BranchId = BranchId.HasValue &&
                   BranchOptions.Any(x => x.Id == BranchId.Value)
            ? BranchId
            : null;

        DepartmentId = DepartmentId.HasValue &&
                       DepartmentOptions.Any(x =>
                           x.Id == DepartmentId.Value)
            ? DepartmentId
            : null;

        var result = await _employeeService.GetPagedAsync(
            new EmployeeListQueryViewModel
            {
                DataScope = directoryScope,
                CompanyId = CompanyId,
                SearchTerm = SearchTerm,
                BranchId = BranchId,
                DepartmentId = DepartmentId,
                StatusFilter = StatusFilter,
                SortBy = SortBy,
                PageNumber = PageNumber,
                PageSize = PageSize
            });

        var profileScope = await _permissionAuthorizationService
            .GetPeopleDataScopeAsync(
                systemUserId,
                PeoplePermissionCodes.ViewProfile,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.ViewProfile),
                HttpContext.RequestAborted);

        // Unify the Access Roles Employees data scope with the rules-based scope:
        // a row is viewable only if BOTH allow it. Admin, no Data role, or an
        // "All" scope resolve to Unrestricted, so this can only tighten.
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase),
            HttpContext.RequestAborted);

        foreach (var employee in result.Items)
        {
            employee.CanViewProfile = profileScope.AllowsEmployee(
                    employee.Id,
                    employee.CompanyId,
                    employee.BranchId,
                    employee.DepartmentId)
                && accessRoleScope.AllowsEmployee(
                    employee.Id,
                    employee.CompanyId,
                    employee.BranchId,
                    employee.DepartmentId);
        }

        Employees = result.Items;
        TotalEmployees = result.TotalEmployees;
        FilteredEmployees = result.FilteredEmployees;
        ActiveEmployees = result.ActiveEmployees;
        InactiveEmployees = result.InactiveEmployees;
        NewThisYear = result.NewThisYear;
        PageNumber = result.PageNumber;
        PageSize = result.PageSize;
        TotalPages = result.TotalPages;
    }

    private static int NormalizePageSize(int value)
    {
        return value switch
        {
            10 => 10,
            50 => 50,
            100 => 100,
            _ => 25
        };
    }

    private static string NormalizeStatusFilter(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "inactive" => "inactive",
            _ => "active"
        };
    }

    private static string NormalizeSortBy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "code" => "code",
            "branch" => "branch",
            "department" => "department",
            "hiredate" => "hiredate",
            "status" => "status",
            _ => "name"
        };
    }

    private static IReadOnlyList<int?> BuildPaginationPages(
        int currentPage,
        int totalPages)
    {
        if (totalPages <= 0)
        {
            return Array.Empty<int?>();
        }

        if (totalPages <= 7)
        {
            return Enumerable
                .Range(1, totalPages)
                .Select(x => (int?)x)
                .ToList();
        }

        var pages = new List<int?> { 1 };

        if (currentPage > 4)
        {
            pages.Add(null);
        }

        var start = Math.Max(2, currentPage - 1);
        var end = Math.Min(totalPages - 1, currentPage + 1);

        if (currentPage <= 4)
        {
            start = 2;
            end = 5;
        }
        else if (currentPage >= totalPages - 3)
        {
            start = totalPages - 4;
            end = totalPages - 1;
        }

        for (var pageNumber = start;
             pageNumber <= end;
             pageNumber++)
        {
            pages.Add(pageNumber);
        }

        if (currentPage < totalPages - 3)
        {
            pages.Add(null);
        }

        pages.Add(totalPages);
        return pages;
    }
}
