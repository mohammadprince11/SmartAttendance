using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class SystemUser : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    // Role is only a general label/template.
    // Real access is controlled by SystemUserPermissions.
    public SystemUserRole Role { get; set; } = SystemUserRole.Viewer;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    // Optional link to Employee so HR can assign permissions directly to an employee.
    public int? EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public ICollection<SystemUserPermission> UserPermissions { get; set; } = new List<SystemUserPermission>();
}
