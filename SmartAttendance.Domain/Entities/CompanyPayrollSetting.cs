using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class CompanyPayrollSetting : AuditableEntity
{
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public PayrollFrequency PayrollFrequency { get; set; } = PayrollFrequency.Monthly;

    public int PeriodStartDay { get; set; } = 1;

    public int PeriodEndDay { get; set; } = 30;

    public int? PaymentDay { get; set; }

    public bool IsActive { get; set; } = true;
}