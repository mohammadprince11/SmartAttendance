using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

public sealed class EndServiceAccessService(IServiceScopeFactory scopeFactory, ILogger<EndServiceAccessService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup catch-up, then periodic closure. Authorization never depends on this timer.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await EndServiceAccessStore.CloseDueAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("End-service account closure failed; request deadlines remain enforced. Retrying."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
