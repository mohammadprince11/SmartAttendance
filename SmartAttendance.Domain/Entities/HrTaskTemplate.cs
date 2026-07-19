using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A checklist template item for onboarding/offboarding. Launching a process for
/// an employee copies the active templates into EmployeeTask rows.
/// </summary>
public class HrTaskTemplate : AuditableEntity
{
    public HrProcessType ProcessType { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Responsible party label (HR / IT / المدير المباشر / المالية...).</summary>
    public string? AssigneeRole { get; set; }

    /// <summary>Due offset in days from the process start date.</summary>
    public int DueDays { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
