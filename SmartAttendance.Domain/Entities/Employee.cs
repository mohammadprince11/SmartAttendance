using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Employee : AuditableEntity
{
    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? NationalId { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Position { get; set; }

    public DateOnly HireDate { get; set; }

    public DateOnly? BirthDate { get; set; }

    public bool IsActive { get; set; } = true;

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;
}
