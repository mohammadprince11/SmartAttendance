namespace SmartAttendance.Application.Setup.ViewModels;

public class BulkAssignShiftViewModel
{
    public int ShiftId { get; set; }

    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public bool OnlyEmployeesWithoutCurrentShift { get; set; } = true;

    // Examples: Friday / Friday,Saturday
    public string? WeeklyOffDays { get; set; }
}
