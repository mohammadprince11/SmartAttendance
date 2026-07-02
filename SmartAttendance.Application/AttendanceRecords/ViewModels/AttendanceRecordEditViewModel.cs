using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.AttendanceRecords.ViewModels;

public class AttendanceRecordEditViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly AttendanceDate { get; set; }

    public DateTime CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public AttendanceSource Source { get; set; }

    public AttendanceStatus Status { get; set; }

    public int? DeviceId { get; set; }

    public string? Notes { get; set; }
}
