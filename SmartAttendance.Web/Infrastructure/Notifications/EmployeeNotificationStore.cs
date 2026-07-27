using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// صندوق وارد إشعارات الموظف (بوابة الموبايل PWA) فوق <see cref="UserNotification"/> +
/// <see cref="UserNotificationRecipient"/> عبر EF. يحلّ فجوة موازية لجرس الـback-office:
/// الجدول كان يُكتَب (المولّد الكرون) ولا يُقرأ بأي واجهة. ميزة هذه القناة: حالة القراءة
/// <b>لكل موظف</b> (عمود IsRead على المستلم) لا عمود واحد مشترك.
/// </summary>
public static class EmployeeNotificationStore
{
    public sealed record InboxItem(int Id, string Title, string Message, string Url, bool IsRead, DateTime CreatedAt);

    public sealed record InboxView(int UnreadCount, IReadOnlyList<InboxItem> Items);

    public static readonly InboxView Empty = new(0, Array.Empty<InboxItem>());

    public static async Task<InboxView> GetForEmployeeAsync(
        ApplicationDbContext db, int employeeId, int take = 8, CancellationToken ct = default)
    {
        if (employeeId <= 0)
        {
            return Empty;
        }

        var query = db.Set<UserNotificationRecipient>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId);

        var unread = await query.CountAsync(r => !r.IsRead, ct);

        var items = await query
            .OrderByDescending(r => r.UserNotification.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new InboxItem(
                r.UserNotification.Id,
                r.UserNotification.TitleAr ?? string.Empty,
                r.UserNotification.MessageAr ?? string.Empty,
                r.UserNotification.Url ?? string.Empty,
                r.IsRead,
                r.UserNotification.CreatedAtUtc))
            .ToListAsync(ct);

        return new InboxView(unread, items);
    }

    /// <summary>يعلّم إشعاراً واحداً مقروءاً لهذا الموظف فقط. يعيد عدد الصفوف المتأثرة.</summary>
    public static async Task<int> MarkReadAsync(
        ApplicationDbContext db, int employeeId, int notificationId, CancellationToken ct = default)
    {
        if (employeeId <= 0 || notificationId <= 0)
        {
            return 0;
        }

        var recipient = await db.Set<UserNotificationRecipient>()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId
                && r.UserNotificationId == notificationId && !r.IsRead, ct);
        if (recipient is null)
        {
            return 0;
        }

        recipient.IsRead = true;
        recipient.ReadAtUtc = DateTime.UtcNow;
        return await db.SaveChangesAsync(ct);
    }

    /// <summary>يعلّم كل إشعارات الموظف غير المقروءة مقروءةً. يعيد عدد الصفوف المتأثرة.</summary>
    public static async Task<int> MarkAllReadAsync(
        ApplicationDbContext db, int employeeId, CancellationToken ct = default)
    {
        if (employeeId <= 0)
        {
            return 0;
        }

        var unread = await db.Set<UserNotificationRecipient>()
            .Where(r => r.EmployeeId == employeeId && !r.IsRead)
            .ToListAsync(ct);
        if (unread.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var recipient in unread)
        {
            recipient.IsRead = true;
            recipient.ReadAtUtc = now;
        }
        return await db.SaveChangesAsync(ct);
    }
}
