using System.ComponentModel.DataAnnotations;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.Setup.ViewModels;

public class CompanyPayrollSettingsViewModel
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public PayrollFrequency PayrollFrequency { get; set; } = PayrollFrequency.Monthly;

    [Range(1, 31)]
    public int PeriodStartDay { get; set; } = 1;

    [Range(1, 31)]
    public int PeriodEndDay { get; set; } = 30;

    [Range(1, 31)]
    public int? PaymentDay { get; set; }

    public bool IsActive { get; set; } = true;
}