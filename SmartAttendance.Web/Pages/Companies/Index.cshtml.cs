using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Companies;

public class IndexModel : PageModel
{
    private readonly ICompanyService _companyService;

    public IndexModel(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Companies = await _companyService.GetAllAsync(SearchTerm);
    }
}