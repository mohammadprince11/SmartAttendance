using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Permissions.ViewModels;

namespace SmartAttendance.Web.Pages.Permissions;

public class IndexModel : PageModel
{
    private readonly IPermissionService _permissionService;

    public IndexModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public IEnumerable<PermissionListViewModel> Permissions { get; set; } = new List<PermissionListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Permissions = await _permissionService.GetAllAsync(SearchTerm);
    }

    public async Task<IActionResult> OnPostSeedAsync()
    {
        var added = await _permissionService.SeedDefaultPermissionsAsync();

        TempData["SuccessMessage"] = added == 0
            ? "Default permissions already exist."
            : $"{added} default permissions added successfully.";

        return RedirectToPage("./Index");
    }
}
