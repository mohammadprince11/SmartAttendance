using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.SystemUsers.ViewModels;

public class SystemUserDetailsViewModel
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public SystemUserRole Role { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }
}
