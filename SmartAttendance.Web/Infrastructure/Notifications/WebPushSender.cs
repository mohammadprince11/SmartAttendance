using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;
using WebPush;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// قناة Web-Push عبر حزمة WebPush (تعمية VAPID + حمولة مشفّرة). تدفع لكل أجهزة الموظف
/// المشتركة، وتحذف الاشتراك تلقائياً حين يرفضه المزوّد (404/410 = منتهٍ). لا ترمي:
/// تبتلع أخطاء الجهاز الواحد لئلا يُسقط فشلُ جهازٍ بقية الأجهزة.
/// </summary>
public sealed class WebPushSender : IWebPushSender
{
    private readonly VapidOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushClient _client = new();

    public WebPushSender(IOptions<VapidOptions> options, ILogger<WebPushSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsUsable;

    public async Task<int> SendToEmployeeAsync(ApplicationDbContext dbContext, int employeeId, PushPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsUsable) return 0;

        var subscriptions = await PushSubscriptionStore.ListByEmployeeAsync(dbContext, employeeId);
        if (subscriptions.Count == 0) return 0;

        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        var json = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, url = payload.Url });

        int delivered = 0;
        foreach (var sub in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var pushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await _client.SendNotificationAsync(pushSub, json, vapid, cancellationToken);
                delivered++;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // الاشتراك منتهٍ/محذوف من الجهاز — نظّفه فلا نعيد المحاولة عليه.
                await PushSubscriptionStore.DeleteAsync(dbContext, sub.Endpoint);
                _logger.LogInformation("حُذف اشتراك دفع منتهٍ للموظف {Emp}.", employeeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "فشل دفع إشعار لجهاز الموظف {Emp}.", employeeId);
            }
        }

        return delivered;
    }
}
