namespace SmartAttendance.Application.EmployeePermissions.ViewModels;

public class EmployeePermissionEmployeeViewModel
{
    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool IsActive { get; set; }

    public bool HasSystemUser { get; set; }
}
