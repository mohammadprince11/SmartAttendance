using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class EmployeeViolationCase : AuditableEntity
{
    public string ReferenceNo { get; set; } = string.Empty;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string ViolationCategory { get; set; } = string.Empty;

    public string ViolationTitle { get; set; } = string.Empty;

    public DateTime EventDate { get; set; }

    public string Source { get; set; } = "مباشر";

    public string ActionStatus { get; set; } = "بانتظار الإجراء";

    public string Status { get; set; } = "مسودة";

    public string? ProposedAction { get; set; }

    public string? Notes { get; set; }

    public string? FinalAction { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? ClosedAt { get; set; }
}
