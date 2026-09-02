using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A language enabled by one tenant for translatable business data. UI-language
/// catalogs remain global; this entity controls which values a company must enter.
/// </summary>
public sealed class CompanyLanguage : AuditableEntity
{
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public string CultureCode { get; set; } = string.Empty;

    public string NativeName { get; set; } = string.Empty;

    public string EnglishName { get; set; } = string.Empty;

    public string Direction { get; set; } = "ltr";

    public bool IsDefault { get; set; }

    public bool IsRequired { get; set; } = true;

    public bool IsActive { get; set; } = true;
}
