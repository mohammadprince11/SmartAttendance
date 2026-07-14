using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementComment : AuditableEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public AnnouncementCommentStatus Status { get; set; } = AnnouncementCommentStatus.Published;

    public DateTime? HiddenAtUtc { get; set; }

    public string? HiddenBy { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
