using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Permission : AuditableEntity
{
    public string Module { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
