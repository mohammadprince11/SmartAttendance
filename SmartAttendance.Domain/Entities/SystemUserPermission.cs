using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class SystemUserPermission : AuditableEntity
{
    public int SystemUserId { get; set; }

    public SystemUser SystemUser { get; set; } = null!;

    public int PermissionId { get; set; }

    public Permission Permission { get; set; } = null!;
}
