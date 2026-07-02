namespace SmartAttendance.Application.EmployeePermissions.ViewModels;

public class EmployeePermissionAssignmentViewModel
{
    public int EmployeeId { get; set; }

    public int? SystemUserId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string UserName { get; set; } = string.Empty;

    public bool HasSystemUser { get; set; }

    public List<EmployeePermissionGroupViewModel> Groups { get; set; } = new();
}
