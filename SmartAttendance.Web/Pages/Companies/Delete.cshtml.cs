using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Companies;

public class DeleteModel : PageModel
{
    private readonly ICompanyService _companyService;

    public DeleteModel(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    public CompanyDetailsViewModel Company { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var company = await _companyService.GetByIdAsync(id);

        if (company == null)
            return NotFound();

        Company = company;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _companyService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Company not found or could not be deleted.";

            var company = await _companyService.GetByIdAsync(id);
            if (company != null)
                Company = company;

            return Page();
        }

        TempData["SuccessMessage"] = "Company deleted successfully.";

        return RedirectToPage("./Index");
    }
}