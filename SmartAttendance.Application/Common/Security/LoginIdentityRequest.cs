namespace SmartAttendance.Application.Common.Security;

public sealed class LoginIdentityRequest
{
    public int? EmployeeId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string CompatibilityRole { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}
