using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>حمولة إشعار دفع تصل الجهاز — يقرؤها الـService Worker في حدث push.</summary>
public sealed record PushPayload(string Title, string Body, string? Url = null);

/// <summary>
/// مجرِّد قناة Web-Push. حين تكون معطّلة (VAPID ناقص) يُحقَن <see cref="NoOpWebPushSender"/>.
/// </summary>
public interface IWebPushSender
{
    bool IsEnabled { get; }

    /// <summary>يدفع لكل أجهزة الموظف المشتركة. يعيد عدد الأجهزة التي وصلها الإشعار.</summary>
    Task<int> SendToEmployeeAsync(ApplicationDbContext dbContext, int employeeId, PushPayload payload,
        CancellationToken cancellationToken = default);
}

/// <summary>قناة دفع معطّلة — لا تدفع شيئاً (VAPID غير مُعدّ).</summary>
public sealed class NoOpWebPushSender : IWebPushSender
{
    public bool IsEnabled => false;

    public Task<int> SendToEmployeeAsync(ApplicationDbContext dbContext, int employeeId, PushPayload payload,
        CancellationToken cancellationToken = default) => Task.FromResult(0);
}
