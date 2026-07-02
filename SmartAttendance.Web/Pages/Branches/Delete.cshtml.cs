using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;

namespace SmartAttendance.Web.Pages.Branches;

public class DeleteModel : PageModel
{
    private readonly IBranchService _branchService;

    public DeleteModel(IBranchService branchService)
    {
        _branchService = branchService;
    }

    public BranchDetailsViewModel Branch { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var branch = await _branchService.GetByIdAsync(id);

        if (branch == null)
            return NotFound();

        Branch = branch;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _branchService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Branch not found or could not be deleted.";

            var branch = await _branchService.GetByIdAsync(id);
            if (branch != null)
                Branch = branch;

            return Page();
        }

        TempData["SuccessMessage"] = "Branch deleted successfully.";

        return RedirectToPage("./Index");
    }
}