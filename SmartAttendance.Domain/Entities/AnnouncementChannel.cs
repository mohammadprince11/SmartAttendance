using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementChannel : BaseEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public AnnouncementChannelType ChannelType { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime? ActivatedAtUtc { get; set; }
}
