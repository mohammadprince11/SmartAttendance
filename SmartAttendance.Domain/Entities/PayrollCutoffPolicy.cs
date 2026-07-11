using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class PayrollCutoffPolicy : AuditableEntity
{
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public PayrollCutoffType PolicyType { get; set; }

    public PayrollCutoffBasis CutoffBasis { get; set; } = PayrollCutoffBasis.DayOfMonth;

    public int? DayOfMonth { get; set; }

    public int? OffsetDays { get; set; }

    public TimeOnly? CutoffTime { get; set; }

    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public DateOnly? EffectiveTo { get; set; }

    public int Priority { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}