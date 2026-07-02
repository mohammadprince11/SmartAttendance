using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Permissions.ViewModels;

namespace SmartAttendance.Web.Pages.Permissions;

public class CreateModel : PageModel
{
    private readonly IPermissionService _permissionService;

    public CreateModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [BindProperty]
    public PermissionCreateViewModel Permission { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var created = await _permissionService.CreateAsync(Permission);

        if (!created)
        {
            ErrorMessage = "Permission could not be created. Code may already exist.";
            return Page();
        }

        TempData["SuccessMessage"] = "Permission created successfully.";

        return RedirectToPage("./Index");
    }
}
