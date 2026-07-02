namespace SmartAttendance.Application.AttendanceReports.ViewModels;

public class MonthlyAttendanceReportViewModel
{
    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public int TotalDays { get; set; }

    public int PresentDays { get; set; }

    public int LateDays { get; set; }

    public int AbsentDays { get; set; }

    public int LeaveDays { get; set; }

    public int HolidayDays { get; set; }

    public int EarlyLeaveDays { get; set; }

    public int MissingCheckOutDays { get; set; }

    public int TotalLateMinutes { get; set; }

    public int TotalEarlyLeaveMinutes { get; set; }

    public decimal TotalWorkingHours { get; set; }
}
