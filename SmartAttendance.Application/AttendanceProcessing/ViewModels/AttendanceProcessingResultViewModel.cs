namespace SmartAttendance.Application.AttendanceProcessing.ViewModels;

public class AttendanceProcessingResultViewModel
{
    public int AttendanceRecordId { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public DateOnly AttendanceDate { get; set; }

    public string ShiftCode { get; set; } = string.Empty;

    public string ShiftName { get; set; } = string.Empty;

    public TimeOnly? ShiftStartTime { get; set; }

    public TimeOnly? ShiftEndTime { get; set; }

    public string? WeeklyOffDays { get; set; }

    public bool IsWeeklyOff { get; set; }

    public DateTime? CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public decimal? WorkingHours { get; set; }

    public int LateMinutes { get; set; }

    public int? EarlyLeaveMinutes { get; set; }

    public bool MissingCheckOut { get; set; }

    public string Source { get; set; } = string.Empty;

    public string OriginalStatus { get; set; } = string.Empty;

    public string CalculatedStatus { get; set; } = string.Empty;

    public string? LeaveType { get; set; }

    public string? HolidayName { get; set; }

    public string? Notes { get; set; }
}
