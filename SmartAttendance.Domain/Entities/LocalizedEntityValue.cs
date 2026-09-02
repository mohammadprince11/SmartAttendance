using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A tenant-scoped translation for one translatable field on a business entity.
/// The generic shape lets new languages and entity types be added without columns.
/// </summary>
public sealed class LocalizedEntityValue : AuditableEntity
{
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string CultureCode { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string TranslationStatus { get; set; } = "Manual";
}
