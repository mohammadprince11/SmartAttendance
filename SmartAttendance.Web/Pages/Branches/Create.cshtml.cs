using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Branches;

public class CreateModel : PageModel
{
    private readonly IBranchService _branchService;

    public CreateModel(IBranchService branchService)
    {
        _branchService = branchService;
    }

    [BindProperty]
    public BranchCreateViewModel Branch { get; set; } = new();

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Branch.Code");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Branch.Code");

        if (!ModelState.IsValid)
            return Page();

        var created = await _branchService.CreateAsync(Branch);

        if (!created)
        {
            ErrorMessage = "Branch code already exists or selected company is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Branch created successfully.";

        return RedirectToPage("./Index");
    }
}