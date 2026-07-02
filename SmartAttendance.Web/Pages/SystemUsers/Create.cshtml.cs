using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Application.SystemUsers.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages.SystemUsers;

public class CreateModel : PageModel
{
    private readonly ISystemUserService _systemUserService;

    public CreateModel(ISystemUserService systemUserService)
    {
        _systemUserService = systemUserService;
    }

    [BindProperty]
    public SystemUserCreateViewModel SystemUser { get; set; } = new();

    public IEnumerable<SelectListItem> Roles { get; set; } = new List<SelectListItem>();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        LoadRoles();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoadRoles();

        if (!ModelState.IsValid)
            return Page();

        var created = await _systemUserService.CreateAsync(SystemUser);

        if (!created)
        {
            ErrorMessage = "User could not be created. User name may already exist.";
            return Page();
        }

        TempData["SuccessMessage"] = "System user created successfully.";

        return RedirectToPage("./Index");
    }

    private void LoadRoles()
    {
        Roles = Enum.GetValues<SystemUserRole>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
