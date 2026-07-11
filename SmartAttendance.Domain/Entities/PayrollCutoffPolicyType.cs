using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class PayrollCutoffPolicyType : AuditableEntity
{
    public int PayrollCutoffPolicyId { get; set; }

    public PayrollCutoffPolicy PayrollCutoffPolicy { get; set; } = null!;

    public PayrollCutoffType PolicyType { get; set; }
}