using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementTemplate : AuditableEntity
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string LanguageCode { get; set; } = "ar";

    public string TitleTemplate { get; set; } = string.Empty;

    public string BodyTemplate { get; set; } = string.Empty;

    public string? Category { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSystem { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<AnnouncementContent> Contents { get; set; } = new List<AnnouncementContent>();
}
