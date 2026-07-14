using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementGroup : AuditableEntity
{
    public Guid TranslationGroupId { get; set; } = Guid.NewGuid();

    public int? LegacyAnnouncementId { get; set; }

    public int? CreatedBySystemUserId { get; set; }

    public SystemUser? CreatedBySystemUser { get; set; }

    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Pending;

    public DateOnly? PublishDate { get; set; }

    public DateOnly? ExpireDate { get; set; }

    public AnnouncementExpirationBehavior? ExpirationBehavior { get; set; }

    public bool CommentsEnabled { get; set; }

    public bool ReactionsEnabled { get; set; } = true;

    public DateTime? AudienceCapturedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<AnnouncementContent> Contents { get; set; } = new List<AnnouncementContent>();

    public ICollection<AnnouncementAudienceRule> AudienceRules { get; set; } = new List<AnnouncementAudienceRule>();

    public ICollection<AnnouncementRecipient> Recipients { get; set; } = new List<AnnouncementRecipient>();

    public ICollection<AnnouncementChannel> Channels { get; set; } = new List<AnnouncementChannel>();

    public ICollection<AnnouncementReadReceipt> ReadReceipts { get; set; } = new List<AnnouncementReadReceipt>();

    public ICollection<AnnouncementComment> Comments { get; set; } = new List<AnnouncementComment>();

    public ICollection<AnnouncementReaction> Reactions { get; set; } = new List<AnnouncementReaction>();

    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
}
