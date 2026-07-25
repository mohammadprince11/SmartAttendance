namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>رسالة بريد واحدة جاهزة للإرسال.</summary>
public sealed record EmailMessage(string ToAddress, string Subject, string Body);

/// <summary>نتيجة محاولة إرسال: نجاح، أو فشل برسالة خطأ للتسجيل بالصادر.</summary>
public sealed record EmailResult(bool Sent, string? Error)
{
    public static EmailResult Ok { get; } = new(true, null);
    public static EmailResult Fail(string error) => new(false, error);
}

/// <summary>
/// مجرِّد قناة البريد — يفصل مؤلّف الإشعارات عن آلية النقل (SMTP الآن، أو مزوّد
/// سحابي لاحقاً). حين تكون القناة معطّلة يُحقَن <see cref="NoOpEmailSender"/>.
/// </summary>
public interface IEmailSender
{
    /// <summary>هل القناة قادرة على الإرسال فعلياً الآن؟ (معطّلة ⟹ false).</summary>
    bool IsEnabled { get; }

    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>مرسِل معطّل — يُستخدم حين لا يكون SMTP مُعدّاً؛ لا يُرسِل شيئاً.</summary>
public sealed class NoOpEmailSender : IEmailSender
{
    public bool IsEnabled => false;

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(EmailResult.Fail("قناة البريد غير مفعّلة (SMTP:Enabled=false أو ناقص الإعداد)."));
}
