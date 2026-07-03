namespace SmartAttendance.Application.EmployeeShifts.ViewModels;

public class EmployeeShiftListViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public int ShiftId { get; set; }

    public string ShiftCode { get; set; } = string.Empty;

    public string ShiftName { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsCurrent { get; set; }

    public string? WeeklyOffDays { get; set; }
}
