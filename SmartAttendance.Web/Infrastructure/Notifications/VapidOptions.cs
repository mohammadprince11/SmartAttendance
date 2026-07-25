namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// إعداد Web-Push (VAPID) — يُقرأ من قسم "WebPush" في appsettings/متغيرات البيئة.
/// مفاتيح VAPID تُولَّد مرة (VapidHelper.GenerateVapidKeys) وتُخزَّن هنا. حين تكون
/// ناقصة تكون القناة معطّلة (نفس فلسفة SMTP: تُفعَّل عمداً لا ضمناً).
/// </summary>
public sealed class VapidOptions
{
    public const string SectionName = "WebPush";

    /// <summary>هوية المُرسِل — بريد mailto: أو رابط الموقع (يطلبه بروتوكول VAPID).</summary>
    public string Subject { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>القناة جاهزة للإرسال؟ (الهوية + المفتاحان موجودة).</summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Subject)
        && !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey);
}
