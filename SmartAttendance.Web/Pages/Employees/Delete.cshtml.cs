using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;

namespace SmartAttendance.Web.Pages.Employees;

public class DeleteModel : PageModel
{
    private readonly IEmployeeService _employeeService;

    public DeleteModel(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public EmployeeDetailsViewModel Employee { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var employee = await _employeeService.GetByIdAsync(id);

        if (employee == null)
            return NotFound();

        Employee = employee;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _employeeService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Employee not found or could not be deleted.";

            var employee = await _employeeService.GetByIdAsync(id);
            if (employee != null)
                Employee = employee;

            return Page();
        }

        TempData["SuccessMessage"] = "Employee deleted successfully.";

        return RedirectToPage("./Index");
    }
}
