using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.SystemUsers.ViewModels;

public class SystemUserCreateViewModel
{
    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public SystemUserRole Role { get; set; } = SystemUserRole.Viewer;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
