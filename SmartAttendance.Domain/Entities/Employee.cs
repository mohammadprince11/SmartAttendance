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

    public int? PositionId { get; set; }

    public DateOnly HireDate { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? Country { get; set; }

    public string? Nationality { get; set; }

    public string? Gender { get; set; }

    public string? MaritalStatus { get; set; }

    public string? PhotoPath { get; set; }

    public string? ContractType { get; set; }

    public DateOnly? ContractEndDate { get; set; }

    public string? EmploymentStatus { get; set; }

    public int? DirectManagerId { get; set; }

    public DateOnly? ServiceEndDate { get; set; }

    public string? ServiceEndType { get; set; }

    public string? ServiceEndReason { get; set; }

    public string? ServiceEndNotes { get; set; }

    public string? ClearanceStatus { get; set; }

    public DateOnly? LastRehireDate { get; set; }

    public string? RehireReason { get; set; }

    public string? RehireNotes { get; set; }

    public int RehireCount { get; set; }

    public bool IsActive { get; set; } = true;

    public int BranchId { get; set; }

    public Branch Branch { get; set; } = null!;

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;
}
