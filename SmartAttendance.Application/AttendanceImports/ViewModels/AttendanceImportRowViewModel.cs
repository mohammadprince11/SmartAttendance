namespace SmartAttendance.Application.AttendanceImports.ViewModels;

public class AttendanceImportRowViewModel
{
    public int? EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public DateOnly? AttendanceDate { get; set; }

    public DateTime? CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public int PunchCount { get; set; }

    public string FunctionTypes { get; set; } = string.Empty;

    public string MachineNames { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool CanImport { get; set; }
}
