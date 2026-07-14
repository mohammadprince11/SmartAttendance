using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementAuditLog : BaseEntity
{
    public int? AnnouncementGroupId { get; set; }

    public Guid? TranslationGroupId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }

    public int? SystemUserId { get; set; }

    public string? UserName { get; set; }

    public string? IpAddress { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
