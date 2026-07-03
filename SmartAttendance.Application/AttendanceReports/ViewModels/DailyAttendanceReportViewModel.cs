namespace SmartAttendance.Application.AttendanceReports.ViewModels;

public class DailyAttendanceReportViewModel
{
    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string? SearchTerm { get; set; }

    public List<DailyAttendanceReportRowViewModel> Rows { get; set; } = new();

    public int TotalEmployees => Rows.Select(x => x.EmployeeNo).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public int TotalPresent => Rows.Count(x => x.Status.Equals("Present", StringComparison.OrdinalIgnoreCase));

    public int TotalLate => Rows.Count(x => x.Status.Equals("Late", StringComparison.OrdinalIgnoreCase));

    public int TotalAbsent => Rows.Count(x => x.Status.Equals("Absent", StringComparison.OrdinalIgnoreCase));

    public int TotalLeave => Rows.Count(x => x.Status.Equals("Leave", StringComparison.OrdinalIgnoreCase));

    public int TotalHoliday => Rows.Count(x => x.Status.Equals("Holiday", StringComparison.OrdinalIgnoreCase));

    public int TotalMissingCheckOut => Rows.Count(x => x.MissingCheckOut);
}

public class DailyAttendanceReportRowViewModel
{
    public DateOnly AttendanceDate { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public string BranchName { get; set; } = string.Empty;

    public DateTime? CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public bool MissingCheckOut => CheckIn.HasValue && !CheckOut.HasValue;
}
