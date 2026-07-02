using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.AttendanceRecords.ViewModels;

public class AttendanceRecordDetailsViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public DateOnly AttendanceDate { get; set; }

    public DateTime CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public AttendanceSource Source { get; set; }

    public AttendanceStatus Status { get; set; }

    public int? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
