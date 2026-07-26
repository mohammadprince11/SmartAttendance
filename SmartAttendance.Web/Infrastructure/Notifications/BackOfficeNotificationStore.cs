using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// قارئ/كاتب جرس الـback-office فوق جدول <c>SystemNotifications</c> (طبقة HRMS Raw-SQL).
/// يقصر كل استعلام على نطاق المستخدم عبر <see cref="BackOfficeNotificationScope"/> — فلا
/// يرى أو يعلّم أحدٌ صفّاً خارج دوره. ملاحظة تصميمية: <c>IsRead</c> عمودٌ واحد لكل صفّ
/// (لا حالة قراءة لكل مستخدم)، فتعليم صفٍّ موجَّه لدور يعلّمه لكل حامليه — مقبول لـv1
/// بحكم السكيمة القائمة (عادةً مستخدم أدمن/HR واحد).
/// </summary>
public static class BackOfficeNotificationStore
{
    public sealed record BellItem(int Id, string Title, string Message, string Url, bool IsRead, DateTime CreatedAt);

    public sealed record BellView(int UnreadCount, IReadOnlyList<BellItem> Items);

    public static readonly BellView Empty = new(0, Array.Empty<BellItem>());

    /// <summary>يعيد العدّاد غير المقروء + آخر <paramref name="take"/> إشعاراً ضمن نطاق المستخدم.</summary>
    public static async Task<BellView> GetForUserAsync(
        ApplicationDbContext db, BackOfficeNotificationScope.Scope scope, int take = 8)
    {
        if (scope.IsEmpty)
        {
            return Empty;
        }

        var (where, parameters) = BackOfficeNotificationScope.BuildFilter(scope);

        var items = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT TOP (@take) Id, Title, Message, Url, IsRead, CreatedAt
FROM SystemNotifications
WHERE {where}
ORDER BY IsRead ASC, CreatedAt DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@take", take);
                foreach (var (name, value) in parameters)
                {
                    HrmsDatabase.AddParameter(command, name, value);
                }
            },
            reader => new BellItem(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Title"),
                HrmsDatabase.GetString(reader, "Message"),
                HrmsDatabase.GetString(reader, "Url"),
                HrmsDatabase.GetInt(reader, "IsRead") == 1,
                HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.UtcNow));

        var unread = await HrmsDatabase.ScalarAsync<int>(
            db,
            $"SELECT COUNT(*) FROM SystemNotifications WHERE ({where}) AND IsRead = 0;",
            command =>
            {
                foreach (var (name, value) in parameters)
                {
                    HrmsDatabase.AddParameter(command, name, value);
                }
            });

        return new BellView(unread, items);
    }

    /// <summary>يعلّم صفّاً واحداً مقروءاً — فقط إن كان ضمن نطاق المستخدم. يعيد عدد الصفوف المتأثرة.</summary>
    public static async Task<int> MarkReadAsync(
        ApplicationDbContext db, BackOfficeNotificationScope.Scope scope, int id)
    {
        if (scope.IsEmpty || id <= 0)
        {
            return 0;
        }

        var (where, parameters) = BackOfficeNotificationScope.BuildFilter(scope);

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE SystemNotifications SET IsRead = 1
WHERE Id = @id AND IsRead = 0 AND ({where});
SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@id", id);
                foreach (var (name, value) in parameters)
                {
                    HrmsDatabase.AddParameter(command, name, value);
                }
            });
    }

    /// <summary>يعلّم كل غير المقروء ضمن نطاق المستخدم مقروءاً. يعيد عدد الصفوف المتأثرة.</summary>
    public static async Task<int> MarkAllReadAsync(
        ApplicationDbContext db, BackOfficeNotificationScope.Scope scope)
    {
        if (scope.IsEmpty)
        {
            return 0;
        }

        var (where, parameters) = BackOfficeNotificationScope.BuildFilter(scope);

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE SystemNotifications SET IsRead = 1
WHERE IsRead = 0 AND ({where});
SELECT @@ROWCOUNT;
""",
            command =>
            {
                foreach (var (name, value) in parameters)
                {
                    HrmsDatabase.AddParameter(command, name, value);
                }
            });
    }
}
