using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Companies;

public class CreateModel : PageModel
{
    private readonly ICompanyService _companyService;

    public CreateModel(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [BindProperty]
    public CompanyCreateViewModel Company { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var created = await _companyService.CreateAsync(Company);

        if (!created)
        {
            ErrorMessage = "Company code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Company created successfully.";

        return RedirectToPage("./Index");
    }
}