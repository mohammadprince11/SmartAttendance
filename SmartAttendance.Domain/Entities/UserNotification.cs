using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class UserNotification : BaseEntity
{
    public int? AnnouncementGroupId { get; set; }

    public AnnouncementGroup? AnnouncementGroup { get; set; }

    public UserNotificationType NotificationType { get; set; } = UserNotificationType.Announcement;

    public string? TitleAr { get; set; }

    public string? TitleEn { get; set; }

    public string? MessageAr { get; set; }

    public string? MessageEn { get; set; }

    public string? Url { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserNotificationRecipient> Recipients { get; set; } = new List<UserNotificationRecipient>();
}
