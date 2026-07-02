using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Application.SystemUsers.ViewModels;

namespace SmartAttendance.Web.Pages.SystemUsers;

public class DeleteModel : PageModel
{
    private readonly ISystemUserService _systemUserService;

    public DeleteModel(ISystemUserService systemUserService)
    {
        _systemUserService = systemUserService;
    }

    public SystemUserDetailsViewModel SystemUser { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _systemUserService.GetByIdAsync(id);

        if (user == null)
            return NotFound();

        SystemUser = user;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _systemUserService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "User not found or could not be deleted.";

            var user = await _systemUserService.GetByIdAsync(id);
            if (user != null)
                SystemUser = user;

            return Page();
        }

        TempData["SuccessMessage"] = "System user deleted successfully.";

        return RedirectToPage("./Index");
    }
}
