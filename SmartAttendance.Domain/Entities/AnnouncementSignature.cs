using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementSignature : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string LanguageCode { get; set; } = "ar";

    public string Text { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<AnnouncementContent> Contents { get; set; } = new List<AnnouncementContent>();
}
