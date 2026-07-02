namespace SmartAttendance.Application.EmployeeShifts.ViewModels;

public class EmployeeShiftEditViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public int ShiftId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsCurrent { get; set; } = true;
}
