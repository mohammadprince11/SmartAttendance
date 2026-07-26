using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// خدمة خلفية تشغّل مولّد مركز الإشعارات مرّة كل يوم (بتوقيت بغداد، بعد الساعة الهدف)
/// مع «لحاق» عند الإقلاع. تعتمد على <see cref="NotificationRuleGenerator"/> الذي يمنع
/// التكرار بجدول أحداث، فإعادة التشغيل آمنة. تُنشئ نطاقاً لكل دورة وتبتلع الأخطاء لئلا
/// تُسقط المضيف. تعمل دائماً (قناة داخل النظام لا تحتاج SMTP)؛ وتدفع Web-Push إن كان مُفعَّلاً.
/// </summary>
public sealed class NotificationRuleGeneratorService : BackgroundService
{
    private const int RunAtHour = 8; // 08:00 بتوقيت بغداد

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebPushSender _webPush;
    private readonly ILogger<NotificationRuleGeneratorService> _logger;

    private DateOnly _lastRunDate = DateOnly.MinValue;

    public NotificationRuleGeneratorService(
        IServiceScopeFactory scopeFactory,
        IWebPushSender webPush,
        ILogger<NotificationRuleGeneratorService> logger)
    {
        _scopeFactory = scopeFactory;
        _webPush = webPush;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("مولّد مركز الإشعارات يعمل (فحص كل ساعة، إطلاق يومي بعد {Hour}:00 بتوقيت بغداد).", RunAtHour);

        // لحاق أولي عند الإقلاع (بعد مهلة قصيرة لتسخين المضيف)
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await TickAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            var baghdadNow = GetBaghdadNow();
            var today = DateOnly.FromDateTime(baghdadNow.DateTime);

            // يُطلق مرّة واحدة لكل يوم، بعد الساعة الهدف
            if (today <= _lastRunDate || baghdadNow.Hour < RunAtHour)
                return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var result = await NotificationRuleGenerator.GenerateAsync(db, _webPush, today, stoppingToken);
            _lastRunDate = today;

            if (result.NewEvents > 0)
                _logger.LogInformation(
                    "مولّد الإشعارات ({Date}): {Events} حدث جديد، {Created} إشعار، {Push} دفعة Web-Push.",
                    today, result.NewEvents, result.NotificationsCreated, result.PushDelivered);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // إيقاف نظيف
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في دورة مولّد مركز الإشعارات — تُتخطّى وتُعاد لاحقاً.");
        }
    }

    private static DateTimeOffset GetBaghdadNow()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Arabic Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad"); }
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
    }
}
