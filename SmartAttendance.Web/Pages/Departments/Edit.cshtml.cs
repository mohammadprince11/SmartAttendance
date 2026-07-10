using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;

namespace SmartAttendance.Web.Pages.Departments;

public class EditModel : PageModel
{
    private readonly IDepartmentService _departmentService;

    public EditModel(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    [BindProperty]
    public DepartmentEditViewModel Department { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");

        var department = await _departmentService.GetEditByIdAsync(id);

        if (department == null)
            return NotFound();

        Department = department;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");

        if (!ModelState.IsValid)
            return Page();

        var updated = await _departmentService.UpdateAsync(Department);

        if (!updated)
        {
            ErrorMessage = "Department not found, duplicated, or code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Department updated successfully.";

        return RedirectToPage("./Index");
    }
}