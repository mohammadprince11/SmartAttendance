using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Application.SystemUsers.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages.SystemUsers;

public class EditModel : PageModel
{
    private readonly ISystemUserService _systemUserService;

    public EditModel(ISystemUserService systemUserService)
    {
        _systemUserService = systemUserService;
    }

    [BindProperty]
    public SystemUserEditViewModel SystemUser { get; set; } = new();

    public IEnumerable<SelectListItem> Roles { get; set; } = new List<SelectListItem>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        LoadRoles();

        var user = await _systemUserService.GetEditByIdAsync(id);

        if (user == null)
            return NotFound();

        SystemUser = user;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoadRoles();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _systemUserService.UpdateAsync(SystemUser);

        if (!updated)
        {
            ErrorMessage = "User not found or user name already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "System user updated successfully.";

        return RedirectToPage("./Index");
    }

    private void LoadRoles()
    {
        Roles = Enum.GetValues<SystemUserRole>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
