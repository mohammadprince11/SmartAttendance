using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// Per-employee, per-year grant of a leave type: the entitled days plus any days
/// carried over from the previous year. Consumption ("used") is intentionally NOT
/// stored here — it is derived from approved <see cref="LeaveRequest"/> rows so this
/// table stays a pure grant/adjustment record. That keeps the model decoupled: a
/// future accrual ledger can drive the grant side without touching consumption.
/// A missing row means "use the code policy default" (see IraqiLeavePolicy).
/// </summary>
public class LeaveBalance : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public int Year { get; set; }

    public LeaveType LeaveType { get; set; }

    /// <summary>Entitled days for this year (override of the policy default).</summary>
    public decimal EntitledDays { get; set; }

    /// <summary>Days carried over from the previous year.</summary>
    public decimal CarriedOverDays { get; set; }

    public string? Note { get; set; }
}
