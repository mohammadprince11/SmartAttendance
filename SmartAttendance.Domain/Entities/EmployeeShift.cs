using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class EmployeeShift : AuditableEntity
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsCurrent { get; set; } = true;

    // Comma-separated day names, e.g. "Friday" or "Friday,Saturday".
    // Used by Attendance Processing to avoid marking weekly off days as Absent.
    public string? WeeklyOffDays { get; set; }
}
