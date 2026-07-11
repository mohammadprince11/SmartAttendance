using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.ViewModels;
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

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Companies = await _departmentService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Department.Code");

        var department = await _departmentService.GetEditByIdAsync(id);

        if (department == null)
        {
            return NotFound();
        }

        Department = department;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies = await _departmentService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var updated = await _departmentService.UpdateAsync(Department);

        if (!updated)
        {
            ErrorMessage =
                "تعذر تحديث القسم. " +
                "تأكد من الشركة " +
                "وعدم تكرار الاسم أو الكود.";

            return Page();
        }

        TempData["SuccessMessage"] =
            "تم تحديث القسم بنجاح.";

        return RedirectToPage("./Index");
    }
}
