using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>Academic qualification of an employee (360° file panel).</summary>
public class EmployeeEducation : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string? Country { get; set; }

    public string University { get; set; } = string.Empty;

    public string? Degree { get; set; }

    public string? Major { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    /// <summary>Highest / most recent qualification.</summary>
    public bool IsLatest { get; set; }

    public string? Note { get; set; }
}
