using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.ViewModels;
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

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Companies = await _departmentService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Department.Code");
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

        var created = await _departmentService.CreateAsync(Department);

        if (!created)
        {
            ErrorMessage =
                "تعذر إضافة القسم. " +
                "تأكد من الشركة " +
                "وعدم تكرار الاسم أو الكود.";

            return Page();
        }

        TempData["SuccessMessage"] =
            "تمت إضافة القسم بنجاح.";

        return RedirectToPage("./Index");
    }
}
