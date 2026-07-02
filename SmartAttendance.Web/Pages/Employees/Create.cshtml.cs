using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;

namespace SmartAttendance.Web.Pages.Employees;

public class CreateModel : PageModel
{
    private readonly IEmployeeService _employeeService;

    public CreateModel(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [BindProperty]
    public EmployeeCreateViewModel Employee { get; set; } = new();

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var created = await _employeeService.CreateAsync(Employee);

        if (!created)
        {
            ErrorMessage = "Employee number already exists or selected department is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Employee created successfully.";

        return RedirectToPage("./Index");
    }
}
