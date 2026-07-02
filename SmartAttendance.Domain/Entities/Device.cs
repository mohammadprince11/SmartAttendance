using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Device : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 4370;

    public string SerialNumber { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? FirmwareVersion { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public int BranchId { get; set; }

    public Branch Branch { get; set; } = null!;
}