using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementAttachment : AuditableEntity
{
    public int AnnouncementContentId { get; set; }

    public AnnouncementContent AnnouncementContent { get; set; } = null!;

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsInlinePreview { get; set; } = true;
}
