using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;

namespace SmartAttendance.Web.Pages.Departments;

public class IndexModel : PageModel
{
    private readonly IDepartmentService _departmentService;

    public IndexModel(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } =
        new List<DepartmentListViewModel>();

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Departments = await _departmentService.GetAllAsync(SearchTerm);
        Companies = await _departmentService.GetCompaniesForDropdownAsync();
    }
}
