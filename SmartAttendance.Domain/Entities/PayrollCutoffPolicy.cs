using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class PayrollCutoffPolicy : AuditableEntity
{
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public int FromDay { get; set; } = 1;

    public int ToDay { get; set; } = 30;

    public PayrollCutoffType PolicyType { get; set; }

    public PayrollCutoffBasis CutoffBasis { get; set; } = PayrollCutoffBasis.DayOfMonth;

    public int? DayOfMonth { get; set; }

    public int? OffsetDays { get; set; }

    public TimeOnly? CutoffTime { get; set; }

    public DateOnly EffectiveFrom { get; set; } = new(2000, 1, 1);

    public DateOnly? EffectiveTo { get; set; }

    public int Priority { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PayrollCutoffPolicyType> PolicyTypes { get; set; } = new List<PayrollCutoffPolicyType>();
}