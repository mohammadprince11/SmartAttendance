using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Permissions.ViewModels;

namespace SmartAttendance.Web.Pages.Permissions;

public class EditModel : PageModel
{
    private readonly IPermissionService _permissionService;

    public EditModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [BindProperty]
    public PermissionEditViewModel Permission { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var permission = await _permissionService.GetEditByIdAsync(id);

        if (permission == null)
            return NotFound();

        Permission = permission;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var updated = await _permissionService.UpdateAsync(Permission);

        if (!updated)
        {
            ErrorMessage = "Permission not found or code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Permission updated successfully.";

        return RedirectToPage("./Index");
    }
}
