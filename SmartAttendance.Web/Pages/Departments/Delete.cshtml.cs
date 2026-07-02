using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;

namespace SmartAttendance.Web.Pages.Departments;

public class DeleteModel : PageModel
{
    private readonly IDepartmentService _departmentService;

    public DeleteModel(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public DepartmentDetailsViewModel Department { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var department = await _departmentService.GetByIdAsync(id);

        if (department == null)
            return NotFound();

        Department = department;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _departmentService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Department not found or could not be deleted.";

            var department = await _departmentService.GetByIdAsync(id);
            if (department != null)
                Department = department;

            return Page();
        }

        TempData["SuccessMessage"] = "Department deleted successfully.";

        return RedirectToPage("./Index");
    }
}