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

    [Range(1, 31)]
    public int FromDay { get; set; } = 1;

    [Range(1, 31)]
    public int ToDay { get; set; } = 30;

    public List<PayrollCutoffType> PolicyTypes { get; set; } = new();

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}