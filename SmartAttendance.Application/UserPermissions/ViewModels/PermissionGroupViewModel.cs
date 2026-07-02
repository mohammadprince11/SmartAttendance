namespace SmartAttendance.Application.UserPermissions.ViewModels;

public class PermissionGroupViewModel
{
    public string Module { get; set; } = string.Empty;

    public List<PermissionCheckViewModel> Permissions { get; set; } = new();
}
