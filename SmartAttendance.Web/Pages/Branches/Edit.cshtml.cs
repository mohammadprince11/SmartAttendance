using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Branches;

public class EditModel : PageModel
{
    private readonly IBranchService _branchService;

    public EditModel(IBranchService branchService)
    {
        _branchService = branchService;
    }

    [BindProperty]
    public BranchEditViewModel Branch { get; set; } = new();

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();

        var branch = await _branchService.GetEditByIdAsync(id);

        if (branch == null)
            return NotFound();

        Branch = branch;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _branchService.UpdateAsync(Branch);

        if (!updated)
        {
            ErrorMessage = "Branch not found, branch code already exists, or selected company is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Branch updated successfully.";

        return RedirectToPage("./Index");
    }
}