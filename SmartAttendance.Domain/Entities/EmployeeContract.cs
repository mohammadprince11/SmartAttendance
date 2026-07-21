using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// An employment contract — multiple per employee over time (Kayan "عقود الموظفين").
/// A contract is a terms bundle: number, type (limited/unlimited from lookup),
/// active window (open ToDate = unlimited), the signed contract file, and notes.
/// The "current" contract drives expiry alerts; renewals are new rows, never edits
/// of history (transactions-ledger philosophy).
/// </summary>
public class EmployeeContract : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>Contract number — free text or from the auto-reference schema.</summary>
    public string? ContractNo { get; set; }

    /// <summary>Contract type from the "contracttypes" lookup (محدود سنة، غير محدود…).</summary>
    public string ContractType { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }

    /// <summary>Null = unlimited contract (غير محدود).</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>The contract currently in force for the employee.</summary>
    public bool IsCurrent { get; set; }

    public string? Note { get; set; }

    /// <summary>Signed contract file (original name + served path).</summary>
    public string? AttachmentName { get; set; }

    public string? AttachmentPath { get; set; }

    /// <summary>الأيام المتبقية — null للعقود غير المحدودة.</summary>
    public int? RemainingDays(DateOnly today) =>
        ToDate.HasValue ? Math.Max(0, ToDate.Value.DayNumber - today.DayNumber) : null;
}
