using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AttendanceRecord : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public DateOnly AttendanceDate { get; set; }

    public DateTime CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public AttendanceSource Source { get; set; }

    public AttendanceStatus Status { get; set; }

    public int? DeviceId { get; set; }

    public Device? Device { get; set; }

    public string? Notes { get; set; }
}