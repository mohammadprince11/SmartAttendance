using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Shift : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public int GracePeriodMinutes { get; set; }

    public bool IsActive { get; set; } = true;
}