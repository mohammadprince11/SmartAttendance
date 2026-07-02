using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
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

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Branches = await _departmentService.GetBranchesForDropdownAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Branches = await _departmentService.GetBranchesForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var created = await _departmentService.CreateAsync(Department);

        if (!created)
        {
            ErrorMessage = "Department code already exists or selected branch is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Department created successfully.";

        return RedirectToPage("./Index");
    }
}