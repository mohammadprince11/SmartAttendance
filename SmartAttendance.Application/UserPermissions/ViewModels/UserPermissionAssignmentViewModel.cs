namespace SmartAttendance.Application.UserPermissions.ViewModels;

public class UserPermissionAssignmentViewModel
{
    public int SystemUserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public List<PermissionGroupViewModel> Groups { get; set; } = new();
}
