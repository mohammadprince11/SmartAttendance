using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// مرسِل بريد عبر SMTP باستخدام <see cref="System.Net.Mail.SmtpClient"/> (بلا اعتماد
/// حزمة خارجية). يبني الرسالة من <see cref="SmtpOptions"/> ويعيد نتيجة بدل رمي
/// الاستثناء، ليسجّل مؤلّف الصادر الفشل ويعيد المحاولة لاحقاً.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsUsable;

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsUsable)
            return EmailResult.Fail("قناة البريد غير مفعّلة أو ناقصة الإعداد (Host/FromAddress).");
        if (string.IsNullOrWhiteSpace(message.ToAddress))
            return EmailResult.Fail("لا عنوان بريد للمستلم.");

        try
        {
            using var mail = BuildMail(_options, message);
            using var client = BuildClient(_options);
            await client.SendMailAsync(mail, cancellationToken);
            return EmailResult.Ok;
        }
        catch (Exception ex)
        {
            // لا نُسقط الدورة: نعيد الخطأ ليُسجَّل بصف الصادر وتُعاد المحاولة.
            _logger.LogWarning(ex, "فشل إرسال بريد إلى {To}", message.ToAddress);
            return EmailResult.Fail(ex.Message);
        }
    }

    /// <summary>بناء رسالة البريد من الإعداد — منطق نقي قابل للاختبار بلا شبكة.</summary>
    public static MailMessage BuildMail(SmtpOptions options, EmailMessage message)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        mail.To.Add(new MailAddress(message.ToAddress));
        return mail;
    }

    private static SmtpClient BuildClient(SmtpOptions options)
    {
        var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        client.Credentials = string.IsNullOrWhiteSpace(options.User)
            ? CredentialCache.DefaultNetworkCredentials
            : new NetworkCredential(options.User, options.Password);
        return client;
    }
}
