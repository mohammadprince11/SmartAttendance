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
}