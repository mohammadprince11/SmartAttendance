using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class PeopleCompatibilityAccess
{
    public static bool IsAllowed(string? role, string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(role) ||
            string.IsNullOrWhiteSpace(permissionCode) ||
            !PeoplePermissionCodes.All.Contains(permissionCode))
        {
            return false;
        }

        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("HR Manager", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (role.Equals("HR Officer", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("Branch Manager", StringComparison.OrdinalIgnoreCase))
        {
            return !permissionCode.Equals(
                PeoplePermissionCodes.ManagePermissions,
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
