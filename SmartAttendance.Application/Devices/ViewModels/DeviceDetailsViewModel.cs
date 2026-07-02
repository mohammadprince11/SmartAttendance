namespace SmartAttendance.Application.Devices.ViewModels;

public class DeviceDetailsViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? FirmwareVersion { get; set; }

    public bool IsActive { get; set; }

    public bool IsEnabled { get; set; }

    public int BranchId { get; set; }

    public string BranchName { get; set; } = string.Empty;
}
