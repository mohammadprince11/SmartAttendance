namespace SmartAttendance.Application.EmployeePermissions.ViewModels;

public class EmployeePermissionCheckViewModel
{
    public int PermissionId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsGranted { get; set; }
}
