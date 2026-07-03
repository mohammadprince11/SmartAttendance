namespace SmartAttendance.Application.EmployeeShifts.ViewModels;

public class EmployeeShiftCreateViewModel
{
    public int EmployeeId { get; set; }

    public int ShiftId { get; set; }

    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? EffectiveTo { get; set; }

    public bool IsCurrent { get; set; } = true;

    // Examples: Friday / Friday,Saturday
    public string? WeeklyOffDays { get; set; }
}
