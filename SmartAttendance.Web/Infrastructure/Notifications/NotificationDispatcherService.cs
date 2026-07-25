using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// خدمة خلفية تسحب صادر الإشعارات (قناة البريد) دورياً وتسلّمه — فيصبح الإرسال
/// تلقائياً بلا زر يدوي. تعمل فقط حين تكون قناة البريد مفعّلة؛ وإلا تخرج فوراً بلا
/// استهلاك. تنشئ نطاقاً لكل دورة (DbContext نطاقي) وتبتلع الأخطاء لئلا تسقط المضيف.
/// </summary>
public sealed class NotificationDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _sender;
    private readonly SmtpOptions _options;
    private readonly ILogger<NotificationDispatcherService> _logger;

    public NotificationDispatcherService(
        IServiceScopeFactory scopeFactory,
        IEmailSender sender,
        IOptions<SmtpOptions> options,
        ILogger<NotificationDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sender.IsEnabled)
        {
            _logger.LogInformation("خدمة تسليم الإشعارات معطّلة (SMTP غير مفعّل) — لا استطلاع.");
            return;
        }

        var period = TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds));
        _logger.LogInformation("خدمة تسليم الإشعارات تعمل كل {Seconds}ث.", period.TotalSeconds);

        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var (sent, failed) = await NotificationDispatcher.DispatchPendingAsync(
                    db, _sender, _options.BatchSize, _options.MaxAttempts, stoppingToken);
                if (sent > 0 || failed > 0)
                    _logger.LogInformation("تسليم الإشعارات: أُرسل {Sent}، فشل {Failed}.", sent, failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // إيقاف نظيف
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في دورة تسليم الإشعارات — تُتخطّى وتُعاد بالدورة التالية.");
            }
        }
    }
}
