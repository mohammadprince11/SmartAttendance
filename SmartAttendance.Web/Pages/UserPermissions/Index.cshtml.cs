using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.UserPermissions.Services;
using SmartAttendance.Application.UserPermissions.ViewModels;

namespace SmartAttendance.Web.Pages.UserPermissions;

public class IndexModel : PageModel
{
    private readonly IUserPermissionService _userPermissionService;

    public IndexModel(IUserPermissionService userPermissionService)
    {
        _userPermissionService = userPermissionService;
    }

    public IEnumerable<UserPermissionUserViewModel> Users { get; set; } = new List<UserPermissionUserViewModel>();

    public UserPermissionAssignmentViewModel? Assignment { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SystemUserId { get; set; }

    [BindProperty]
    public List<int> SelectedPermissionIds { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostAsync(int systemUserId)
    {
        var saved = await _userPermissionService.SaveAssignmentsAsync(systemUserId, SelectedPermissionIds);

        if (!saved)
        {
            SystemUserId = systemUserId;
            ErrorMessage = "User not found or permissions could not be saved.";
            await LoadPageAsync();
            return Page();
        }

        TempData["SuccessMessage"] = "User permissions saved successfully.";

        return RedirectToPage("./Index", new { SystemUserId = systemUserId });
    }

    private async Task LoadPageAsync()
    {
        Users = await _userPermissionService.GetUsersAsync();

        if (!SystemUserId.HasValue)
        {
            var firstUser = Users.FirstOrDefault();
            if (firstUser != null)
                SystemUserId = firstUser.Id;
        }

        if (SystemUserId.HasValue)
            Assignment = await _userPermissionService.GetAssignmentAsync(SystemUserId.Value);
    }
}
