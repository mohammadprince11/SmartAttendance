namespace SmartAttendance.Application.Shifts.ViewModels;

public class ShiftListViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public decimal WorkingHours { get; set; }

    public int GraceInMinutes { get; set; }

    public int GraceOutMinutes { get; set; }

    public bool IsNightShift { get; set; }

    public bool IsActive { get; set; }
}
