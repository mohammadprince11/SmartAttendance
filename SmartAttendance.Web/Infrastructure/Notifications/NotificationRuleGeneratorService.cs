using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// خدمة خلفية تفحص أحداث العمليات كل دقيقة والتذكيرات الزمنية مرة يومياً بعد الساعة الهدف
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
        _logger.LogInformation("مولّد مركز الإشعارات يعمل (أحداث العمليات كل دقيقة، أحداث التقويم يومياً بعد {Hour}:00 بتوقيت بغداد).", RunAtHour);

        // لحاق أولي عند الإقلاع (بعد مهلة قصيرة لتسخين المضيف)
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await TickAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
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
            var includeCalendarEvents = today > _lastRunDate && baghdadNow.Hour >= RunAtHour;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // القفل على مستوى القاعدة لا العملية. حراسة جدول الأحداث وحدها لا تكفي:
            // نمطها «اقرأ ثم اكتب» فنسختان تقرآن «لم يُولَّد بعد» معاً ثم تكتبان.
            // و`_lastRunDate` حقل داخل العملية — لا يرى النسخ الأخرى إطلاقاً.
            var result = await NotificationRuleGenerator.GenerateAsync(db, _webPush, today, stoppingToken,
                includeCalendarEvents: includeCalendarEvents);

            if (result.NewEvents > 0)
                _logger.LogInformation(
                    "مولّد الإشعارات ({Date}): {Events} حدث جديد، {Created} إشعار، {Push} دفعة Web-Push.",
                    today, result.NewEvents, result.NotificationsCreated, result.PushDelivered);

            // يُختم اليوم فقط عند التشغيل الفعليّ. ختمُه عند التخطّي يجعل هذه النسخة
            // تظنّ اليوم مُنجزاً، فلو تعطّلت النسخة المالكة قبل أن تُنجز لضاع اليوم كله.
            if (result.LockAcquired && includeCalendarEvents) _lastRunDate = today;
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
