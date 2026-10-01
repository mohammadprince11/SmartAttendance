using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Companies;

public class DeleteModel : PageModel
{
    private readonly ICompanyService _companyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(ICompanyService companyService, ApplicationDbContext dbContext,ICompanyScopeProvider companyScope)
    {
        _companyService = companyService;
        _dbContext = dbContext;
        _companyScope=companyScope;
    }

    public CompanyDetailsViewModel Company { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public bool HasLinkedData { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if(!(await _companyScope.GetAsync(HttpContext.RequestAborted)).IsUnrestricted) return Forbid();
        var tenantId = TenantContext.GetTenantId(User);
        if (tenantId is not > 0) return Forbid();
        var company = await _companyService.GetByIdAsync(id, tenantId.Value);

        if (company == null)
            return NotFound();

        Company = company;
        HasLinkedData = await HasCompanyLinkedDataAsync(id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if(!(await _companyScope.GetAsync(HttpContext.RequestAborted)).IsUnrestricted) return Forbid();
        var tenantId = TenantContext.GetTenantId(User);
        if (tenantId is not > 0) return Forbid();
        var company = await _companyService.GetByIdAsync(id, tenantId.Value);
        if (company is null) return NotFound();

        var hasLinkedData = await HasCompanyLinkedDataAsync(id);
        var deleted = await _companyService.DeleteAsync(id, tenantId.Value);

        if (!deleted)
        {
            ErrorMessage = "Company not found or could not be deleted.";

            Company = company;
            HasLinkedData = hasLinkedData;

            return Page();
        }

        TempData["SuccessMessage"] = hasLinkedData
            ? "Company has linked data, so it was hidden safely."
            : "Company had no linked data, so it was permanently deleted.";

        return RedirectToPage("./Index");
    }

    private async Task<bool> HasCompanyLinkedDataAsync(int companyId)
    {
        return await _dbContext.Branches
            .AnyAsync(x => x.CompanyId == companyId);
    }
}
