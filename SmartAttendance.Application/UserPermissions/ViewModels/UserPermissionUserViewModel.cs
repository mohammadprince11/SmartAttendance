namespace SmartAttendance.Application.UserPermissions.ViewModels;

public class UserPermissionUserViewModel
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
