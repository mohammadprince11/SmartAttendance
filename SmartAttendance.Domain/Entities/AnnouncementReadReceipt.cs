using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementReadReceipt : BaseEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public DateTime FirstReadAtUtc { get; set; }

    public DateTime LastOpenedAtUtc { get; set; }

    public DateTime ConfirmedAtUtc { get; set; }

    public DateTime? NotificationReadAtUtc { get; set; }

    public int OpenCount { get; set; } = 1;
}
