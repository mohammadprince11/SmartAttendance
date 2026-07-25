namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// إعداد قناة البريد (SMTP) — يُقرأ من قسم "Smtp" في appsettings/متغيرات البيئة.
/// افتراضياً <see cref="Enabled"/> = false فلا يحاول النظام الإرسال بلا إعداد صريح
/// (نفس فلسفة راية ForceHttps: ميزة الإنتاج تُفعَّل عمداً لا ضمناً).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>هل قناة البريد مفعّلة؟ إن كانت false يعمل مرسِل No-Op (لا شيء يُرسَل).</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    /// <summary>اسم المستخدم للمصادقة (غالباً = عنوان المُرسِل). فارغ = بلا مصادقة.</summary>
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "SmartAttendance";

    /// <summary>STARTTLS/SSL. الافتراضي true (منفذ 587 STARTTLS أو 465 SSL).</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>عدد رسائل الصادر التي تُسحب في دورة التسليم الواحدة.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>فترة استطلاع خدمة التسليم الخلفية بالثواني.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>أقصى عدد محاولات لكل رسالة قبل وسمها فاشلة نهائياً.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>الإعداد صالح للإرسال فعلياً؟ (مفعّل + مضيف + عنوان مُرسِل).</summary>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(FromAddress);
}
