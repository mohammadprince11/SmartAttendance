namespace SmartAttendance.Application.Permissions.ViewModels;

public class PermissionCreateViewModel
{
    public string Module { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
