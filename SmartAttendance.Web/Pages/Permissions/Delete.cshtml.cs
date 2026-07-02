using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Permissions.ViewModels;

namespace SmartAttendance.Web.Pages.Permissions;

public class DeleteModel : PageModel
{
    private readonly IPermissionService _permissionService;

    public DeleteModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public PermissionDetailsViewModel Permission { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var permission = await _permissionService.GetByIdAsync(id);

        if (permission == null)
            return NotFound();

        Permission = permission;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _permissionService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Permission not found or could not be deleted.";

            var permission = await _permissionService.GetByIdAsync(id);
            if (permission != null)
                Permission = permission;

            return Page();
        }

        TempData["SuccessMessage"] = "Permission deleted successfully.";

        return RedirectToPage("./Index");
    }
}
