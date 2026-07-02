using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Companies;

public class EditModel : PageModel
{
    private readonly ICompanyService _companyService;

    public EditModel(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [BindProperty]
    public CompanyEditViewModel Company { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var company = await _companyService.GetEditByIdAsync(id);

        if (company == null)
            return NotFound();

        Company = company;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var updated = await _companyService.UpdateAsync(Company);

        if (!updated)
        {
            ErrorMessage = "Company not found or could not be updated.";
            return Page();
        }

        TempData["SuccessMessage"] = "Company updated successfully.";

        return RedirectToPage("./Index");
    }
}