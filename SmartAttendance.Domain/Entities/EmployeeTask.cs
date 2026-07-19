using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

/// <summary>A concrete checklist task for one employee (onboarding/offboarding).</summary>
public class EmployeeTask : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public HrProcessType ProcessType { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? AssigneeRole { get; set; }

    public DateOnly? DueDate { get; set; }

    public bool IsDone { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? CompletedBy { get; set; }

    public string? Note { get; set; }
}
