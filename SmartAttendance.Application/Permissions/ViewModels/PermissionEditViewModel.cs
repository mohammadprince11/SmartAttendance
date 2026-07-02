namespace SmartAttendance.Application.Permissions.ViewModels;

public class PermissionEditViewModel
{
    public int Id { get; set; }

    public string Module { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }
}
