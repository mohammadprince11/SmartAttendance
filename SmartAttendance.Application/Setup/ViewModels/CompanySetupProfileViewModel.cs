using System.ComponentModel.DataAnnotations;

namespace SmartAttendance.Application.Setup.ViewModels;

public class CompanySetupProfileViewModel
{
    public int CompanyId { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(500)]
    public string? LogoPath { get; set; }

    [StringLength(2, MinimumLength = 2)]
    public string? CountryCode { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; set; }

    [StringLength(100)]
    public string? TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;
}