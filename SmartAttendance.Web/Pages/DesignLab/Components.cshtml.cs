using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.DesignLab;

public class ComponentsModel : PageModel
{
    private readonly IPermissionAuthorizationService _permissions;

    public ComponentsModel(
        IPermissionAuthorizationService permissions)
    {
        _permissions = permissions;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var systemUserId =
            PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;

        var role =
            PeopleAccessContext.GetRole(HttpContext);

        var allowed =
            await _permissions.HasPermissionAsync(
                systemUserId,
                PeoplePermissionCodes.ManagePermissions,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.ManagePermissions),
                HttpContext.RequestAborted);

        if (!allowed)
        {
            return Forbid();
        }

        return Page();
    }
}
