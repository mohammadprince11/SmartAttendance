using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>يفحص SLA دورياً حتى لو لم يفتح أي مستخدم شاشة الموافقات.</summary>
public sealed class ApprovalSlaDispatcherService(
    IServiceScopeFactory scopeFactory,ILogger<ApprovalSlaDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5));
        while(await timer.WaitForNextTickAsync(stoppingToken)) await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope=scopeFactory.CreateAsyncScope();
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var result=await ApprovalWorkflowEngine.ProcessSlaAsync(db);
            if(result.Reminded>0||result.Escalated>0)
                logger.LogInformation("Approval SLA processed {Reminded} reminders and {Escalated} escalations.",result.Reminded,result.Escalated);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){}
        catch(Exception exception)
        {
            logger.LogError(exception,"Approval SLA processing failed; the next interval will retry.");
        }
    }
}
