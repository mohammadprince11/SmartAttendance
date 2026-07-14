using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementReaction : BaseEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public AnnouncementReactionType ReactionType { get; set; }
}
