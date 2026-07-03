namespace SmartAttendance.Application.AttendanceReports.ViewModels;

public class MonthlyAttendanceReportViewModel
{
    /*
     * Compatibility section:
     * These properties are used by the original AttendanceReportService.cs.
     * Do not remove them unless the old report service is replaced completely.
     */
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

    /*
     * Phase 2 advanced monthly report section:
     * These properties are used by /AttendanceReports/Monthly.
     */
    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string? SearchTerm { get; set; }

    public List<MonthlyAttendanceReportRowViewModel> Rows { get; set; } = new();
}

public class MonthlyAttendanceReportRowViewModel
{
    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public string BranchName { get; set; } = string.Empty;

    public int Present { get; set; }

    public int Late { get; set; }

    public int Absent { get; set; }

    public int Leave { get; set; }

    public int Holiday { get; set; }

    public int MissingCheckOut { get; set; }

    public int TotalRecords { get; set; }
}
