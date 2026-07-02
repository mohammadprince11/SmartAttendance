namespace SmartAttendance.Application.EmployeePermissions.ViewModels;

public class EmployeePermissionGroupViewModel
{
    public string Module { get; set; } = string.Empty;

    public List<EmployeePermissionCheckViewModel> Permissions { get; set; } = new();
}
