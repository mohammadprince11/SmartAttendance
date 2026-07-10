using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;

namespace SmartAttendance.Web.Pages.Departments;

public class CreateModel : PageModel
{
    private readonly IDepartmentService _departmentService;

    public CreateModel(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    [BindProperty]
    public DepartmentCreateViewModel Department { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");

        if (!ModelState.IsValid)
            return Page();

        var created = await _departmentService.CreateAsync(Department);

        if (!created)
        {
            ErrorMessage = "Department already exists or code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Department created successfully.";

        return RedirectToPage("./Index");
    }
}