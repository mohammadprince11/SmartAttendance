using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Integrations;

public sealed class WebhookDispatcherOptions
{
    public const string SectionName = "Webhooks";
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 25;
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>يسلّم صندوق webhooks بتوقيع HMAC وإعادة محاولة، مرة واحدة بين نسخ التطبيق.</summary>
public sealed class WebhookDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _clients;
    private readonly IDataProtector _protector;
    private readonly WebhookDispatcherOptions _options;
    private readonly ILogger<WebhookDispatcherService> _logger;

    public WebhookDispatcherService(
        IServiceScopeFactory scopeFactory, IHttpClientFactory clients,
        IDataProtectionProvider dataProtection,
        Microsoft.Extensions.Options.IOptions<WebhookDispatcherOptions> options,
        ILogger<WebhookDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _clients = clients;
        _protector = dataProtection.CreateProtector("ZYNORA.Webhooks.Secret.v1");
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 5, 300)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await SqlDistributedLock.TryRunAsync(db, "ZYNORA.WebhookDispatcher",
                    () => DispatchBatchAsync(db, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Webhook dispatch cycle failed.");
            }
        }
    }

    private async Task DispatchBatchAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var deliveries = await WebhookStore.ClaimAsync(
            db, _options.BatchSize, _options.MaxAttempts, cancellationToken);
        foreach (var delivery in deliveries)
        {
            try
            {
                var endpoint = new Uri(delivery.EndpointUrl, UriKind.Absolute);
                if (!WebhookEndpointPolicy.IsAllowed(endpoint))
                    throw new InvalidOperationException("Webhook endpoint is not an allowed public HTTPS address.");

                var resolved = await Dns.GetHostAddressesAsync(endpoint.DnsSafeHost, cancellationToken);
                if (resolved.Length == 0 || resolved.Any(address => !WebhookEndpointPolicy.IsPublic(address)))
                    throw new InvalidOperationException("Webhook endpoint resolves to a non-public address.");

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var secret = _protector.Unprotect(delivery.ProtectedSecret);
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Zynora-Event", delivery.EventType);
                request.Headers.Add("X-Zynora-Delivery", delivery.Id.ToString());
                request.Headers.Add("Idempotency-Key", delivery.IdempotencyKey);
                request.Headers.Add("X-Zynora-Timestamp", timestamp.ToString());
                request.Headers.Add("X-Zynora-Signature",
                    WebhookSignature.Sign(secret, timestamp, delivery.PayloadJson));

                using var response = await _clients.CreateClient("ZynoraWebhooks")
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 200 and < 300)
                    await WebhookStore.MarkSentAsync(db, delivery.Id, (int)response.StatusCode);
                else
                    await WebhookStore.MarkRetryAsync(db, delivery.Id, delivery.AttemptCount,
                        _options.MaxAttempts, (int)response.StatusCode, $"HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await WebhookStore.MarkRetryAsync(db, delivery.Id, delivery.AttemptCount,
                    _options.MaxAttempts, null, exception.Message);
                _logger.LogWarning(exception, "Webhook delivery {DeliveryId} failed.", delivery.Id);
            }
        }
    }
}
