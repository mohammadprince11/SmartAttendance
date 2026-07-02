using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Application.SystemUsers.ViewModels;

namespace SmartAttendance.Web.Pages.SystemUsers;

public class IndexModel : PageModel
{
    private readonly ISystemUserService _systemUserService;

    public IndexModel(ISystemUserService systemUserService)
    {
        _systemUserService = systemUserService;
    }

    public IEnumerable<SystemUserListViewModel> Users { get; set; } = new List<SystemUserListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Users = await _systemUserService.GetAllAsync(SearchTerm);
    }
}
