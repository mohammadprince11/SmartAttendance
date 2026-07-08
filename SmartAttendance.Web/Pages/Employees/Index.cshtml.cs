using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;

namespace SmartAttendance.Web.Pages.Employees;

public class IndexModel : PageModel
{
    private readonly IEmployeeService _employeeService;

    public IndexModel(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public List<EmployeeListViewModel> Employees { get; set; } = new();

    public List<string> BranchOptions { get; set; } = new();

    public List<string> DepartmentOptions { get; set; } = new();

    public int TotalEmployees { get; set; }

    public int FilteredEmployees { get; set; }

    public int ActiveEmployees { get; set; }

    public int InactiveEmployees { get; set; }

    public int NewThisYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BranchFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DepartmentFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; }

    public async Task OnGetAsync()
    {
        var allEmployees = (await _employeeService.GetAllAsync(null)).ToList();

        TotalEmployees = allEmployees.Count;
        ActiveEmployees = allEmployees.Count(x => x.IsActive);
        InactiveEmployees = allEmployees.Count(x => !x.IsActive);
        NewThisYear = allEmployees.Count(x => x.HireDate.Year == DateTime.Today.Year);

        StatusFilter = NormalizeStatusFilter(StatusFilter);

        BranchOptions = allEmployees
            .Select(x => x.BranchName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        DepartmentOptions = allEmployees
            .Select(x => x.DepartmentName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        // NEXORA_STATUS_UI_DEFAULT_ACTIVE_START
        // Status filtering is handled client-side so inactive rows remain available
        // when the user selects the inactive filter.
        if (string.IsNullOrWhiteSpace(StatusFilter))
        {
            StatusFilter = "active";
        }
        // NEXORA_STATUS_UI_DEFAULT_ACTIVE_END

        var query = allEmployees.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();

            query = query.Where(x =>
                Contains(x.EmployeeNo, term) ||
                Contains(x.FullName, term) ||
                Contains(x.NationalId, term) ||
                Contains(x.Phone, term) ||
                Contains(x.Email, term) ||
                Contains(x.Position, term) ||
                Contains(x.DepartmentCode, term) ||
                Contains(x.DepartmentName, term) ||
                Contains(x.BranchName, term));
        }

        if (!string.IsNullOrWhiteSpace(BranchFilter))
        {
            query = query.Where(x => string.Equals(x.BranchName, BranchFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(DepartmentFilter))
        {
            query = query.Where(x => string.Equals(x.DepartmentName, DepartmentFilter, StringComparison.OrdinalIgnoreCase));
        }
query = (SortBy ?? "name").ToLowerInvariant() switch
        {
            "code" => query.OrderBy(x => x.EmployeeNo),
            "branch" => query.OrderBy(x => x.BranchName).ThenBy(x => x.FullName),
            "department" => query.OrderBy(x => x.DepartmentName).ThenBy(x => x.FullName),
            "hiredate" => query.OrderByDescending(x => x.HireDate),
            "status" => query.OrderByDescending(x => x.IsActive).ThenBy(x => x.FullName),
            _ => query.OrderBy(x => x.FullName)
        };

        Employees = query.ToList();
        FilteredEmployees = Employees.Count;
    }


    private static string NormalizeStatusFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "active";
        }

        var normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "all" => "all",
            "inactive" => "inactive",
            "active" => "active",
            _ => "active"
        };
    }
    private static bool Contains(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

