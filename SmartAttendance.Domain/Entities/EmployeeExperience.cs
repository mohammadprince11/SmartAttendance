using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>Prior work experience of an employee (360° file panel).</summary>
public class EmployeeExperience : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string CompanyName { get; set; } = string.Empty;

    public string? Country { get; set; }

    public string? JobTitle { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? Note { get; set; }
}
