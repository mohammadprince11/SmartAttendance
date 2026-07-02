namespace SmartAttendance.Application.Shifts.ViewModels;

public class ShiftEditViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public decimal WorkingHours { get; set; }

    public int GraceInMinutes { get; set; } = 0;

    public int GraceOutMinutes { get; set; } = 0;

    public bool IsNightShift { get; set; } = false;

    public bool IsActive { get; set; } = true;
}
