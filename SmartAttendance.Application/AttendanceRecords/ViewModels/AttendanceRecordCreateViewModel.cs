using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.AttendanceRecords.ViewModels;

public class AttendanceRecordCreateViewModel
{
    public int EmployeeId { get; set; }

    public DateOnly AttendanceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateTime CheckIn { get; set; } = DateTime.Now;

    public DateTime? CheckOut { get; set; }

    public AttendanceSource Source { get; set; }

    public AttendanceStatus Status { get; set; }

    public int? DeviceId { get; set; }

    public string? Notes { get; set; }
}
