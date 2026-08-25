using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Departments;

public class DeleteModel : PageModel
{
    private readonly IDepartmentService _departmentService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(IDepartmentService departmentService,ApplicationDbContext dbContext,ICompanyScopeProvider companyScope)
    {
        _departmentService = departmentService;
        _dbContext=dbContext;
        _companyScope=companyScope;
    }

    public DepartmentDetailsViewModel Department { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
        var department = await _departmentService.GetByIdAsync(id);

        if (department == null)
            return NotFound();

        Department = department;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
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

    private async Task<bool> CanAccessAsync(int id)
    {
        var scope=await _companyScope.GetAsync(HttpContext.RequestAborted);
        if(scope.IsDeniedAll) return false;
        var allowed=scope.AllowedCompanyIds.ToArray();
        return await _dbContext.Departments.AsNoTracking().AnyAsync(department=>department.Id==id&&
            (scope.IsUnrestricted||allowed.Contains(department.CompanyId)),HttpContext.RequestAborted);
    }
}
