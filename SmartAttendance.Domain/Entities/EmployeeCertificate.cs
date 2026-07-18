using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>Professional certificate / license of an employee (360° file panel).</summary>
public class EmployeeCertificate : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? ReferenceNo { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? Note { get; set; }
}
