using System.ComponentModel.DataAnnotations;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.Setup.ViewModels;

public class PayrollCutoffPolicyViewModel
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public PayrollCutoffType PolicyType { get; set; }

    public PayrollCutoffBasis CutoffBasis { get; set; } = PayrollCutoffBasis.DayOfMonth;

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    [Range(0, 3660)]
    public int? OffsetDays { get; set; }

    public TimeOnly? CutoffTime { get; set; }

    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? EffectiveTo { get; set; }

    [Range(0, 9999)]
    public int Priority { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}