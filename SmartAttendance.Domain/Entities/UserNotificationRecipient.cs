using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class UserNotificationRecipient : BaseEntity
{
    public int UserNotificationId { get; set; }

    public UserNotification UserNotification { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
