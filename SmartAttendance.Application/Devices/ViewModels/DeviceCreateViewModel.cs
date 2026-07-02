namespace SmartAttendance.Application.Devices.ViewModels;

public class DeviceCreateViewModel
{
    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 4370;

    public string SerialNumber { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? FirmwareVersion { get; set; }

    public int BranchId { get; set; }
}
