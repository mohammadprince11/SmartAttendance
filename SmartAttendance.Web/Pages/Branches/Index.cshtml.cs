using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Web.Pages.Branches;

public class IndexModel : PageModel
{
    private readonly IBranchService _branchService;

    public IndexModel(IBranchService branchService)
    {
        _branchService = branchService;
    }

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Branches = await _branchService.GetAllAsync(SearchTerm);
        Companies = await _branchService.GetCompaniesForDropdownAsync();
    }
}
