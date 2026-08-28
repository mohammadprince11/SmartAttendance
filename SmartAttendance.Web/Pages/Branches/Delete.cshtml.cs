using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Branches;

public class DeleteModel : PageModel
{
    private readonly IBranchService _branchService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(IBranchService branchService,ApplicationDbContext dbContext,ICompanyScopeProvider companyScope)
    {
        _branchService = branchService;
        _dbContext=dbContext;
        _companyScope=companyScope;
    }

    public BranchDetailsViewModel Branch { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
        var branch = await _branchService.GetByIdAsync(id);

        if (branch == null)
            return NotFound();

        Branch = branch;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
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

    private async Task<bool> CanAccessAsync(int id)
    {
        var scope=await _companyScope.GetAsync(HttpContext.RequestAborted);
        if(scope.IsDeniedAll) return false;
        var allowed=scope.AllowedCompanyIds.ToArray();
        return await _dbContext.Branches.AsNoTracking().AnyAsync(branch=>branch.Id==id&&
            (scope.IsUnrestricted||allowed.Contains(branch.CompanyId)),HttpContext.RequestAborted);
    }
}
