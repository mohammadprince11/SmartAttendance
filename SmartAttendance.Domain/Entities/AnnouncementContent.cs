using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementContent : AuditableEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public string LanguageCode { get; set; } = "ar";

    public int? AnnouncementTemplateId { get; set; }

    public AnnouncementTemplate? Template { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? Category { get; set; }

    public AnnouncementSignatureType SignatureType { get; set; } = AnnouncementSignatureType.SavedSignature;

    public int? AnnouncementSignatureId { get; set; }

    public AnnouncementSignature? Signature { get; set; }

    public string? SignatureTextSnapshot { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<AnnouncementAttachment> Attachments { get; set; } = new List<AnnouncementAttachment>();
}
