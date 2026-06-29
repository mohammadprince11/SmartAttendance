using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Employee : AuditableEntity
{
    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}