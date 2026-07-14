using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementRecipient : BaseEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public DateTime ResolvedAtUtc { get; set; }

    public string? SourceSummary { get; set; }
}
