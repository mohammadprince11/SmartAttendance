using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Holiday : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public DateOnly HolidayDate { get; set; }

    public bool IsRecurring { get; set; }

    public string? Description { get; set; }
}