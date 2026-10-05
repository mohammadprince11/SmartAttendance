using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// صندوق صادر دائم لآثار الاعتماد النهائي. إنشاء المهمة يحدث داخل معاملة الاعتماد،
/// لذلك لا يمكن أن يصبح الطلب معتمداً نهائياً من دون أثر قابل لإعادة المحاولة.
/// </summary>
public static class ApprovalEffectJobStore
{
    public sealed record PendingJob(int RequestId, string Actor, string? IpAddress, int CompanyId);

    public static async Task ApplyNowAsync(
        ApplicationDbContext db,
        int requestId,
        CompanyScope scope,
        string actor,
        string? ipAddress)
    {
        try
        {
            await HrmsDatabase.ExecuteAsync(db, """
UPDATE ApprovalEffectJobs
SET IpAddress=COALESCE(@IpAddress, IpAddress), UpdatedAtUtc=SYSUTCDATETIME()
WHERE RequestId=@RequestId AND CompletedAtUtc IS NULL;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@IpAddress", (object?)ipAddress ?? DBNull.Value);
            });

            await DataChangeRequestStore.ApplyIfDataChangeAsync(db, requestId, actor, ipAddress);
            await FinancialRequestStore.ApplyIfFinancialAsync(db, scope, requestId, actor, ipAddress);
            await EmployeeLifecycleApprovalStore.ApplyIfLifecycleAsync(db, scope, requestId, actor, ipAddress);
            await ApprovedAttendanceRequestEffectStore.ApplyAsync(db, scope, requestId, actor);

            await HrmsDatabase.ExecuteAsync(db, """
UPDATE ApprovalEffectJobs
SET CompletedAtUtc=SYSUTCDATETIME(), LockedUntilUtc=NULL, LastError=NULL,
    UpdatedAtUtc=SYSUTCDATETIME()
WHERE RequestId=@RequestId AND CompletedAtUtc IS NULL;
""", command => HrmsDatabase.AddParameter(command, "@RequestId", requestId));
        }
        catch (Exception exception)
        {
            var safeError = exception.GetType().Name;
            await HrmsDatabase.ExecuteAsync(db, """
UPDATE ApprovalEffectJobs
SET Attempts=Attempts+1,
    NextAttemptAtUtc=DATEADD(MINUTE,
        CASE WHEN Attempts < 5 THEN POWER(CAST(2 AS float), Attempts) ELSE 30 END,
        SYSUTCDATETIME()),
    LockedUntilUtc=NULL,
    LastError=@Error,
    UpdatedAtUtc=SYSUTCDATETIME()
WHERE RequestId=@RequestId AND CompletedAtUtc IS NULL;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@Error", safeError);
            });
            throw;
        }
    }

    public static Task<List<PendingJob>> ClaimPendingAsync(
        ApplicationDbContext db,
        int take = 20)
    {
        take = Math.Clamp(take, 1, 100);
        return HrmsDatabase.QueryAsync(db, $"""
;WITH pending AS
(
    SELECT TOP ({take}) job.RequestId
    FROM ApprovalEffectJobs job WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE job.CompletedAtUtc IS NULL
      AND job.NextAttemptAtUtc <= SYSUTCDATETIME()
      AND (job.LockedUntilUtc IS NULL OR job.LockedUntilUtc < SYSUTCDATETIME())
    ORDER BY job.NextAttemptAtUtc, job.RequestId
)
UPDATE job
SET LockedUntilUtc=DATEADD(MINUTE, 5, SYSUTCDATETIME()),
    UpdatedAtUtc=SYSUTCDATETIME()
OUTPUT inserted.RequestId, inserted.Actor, inserted.IpAddress,
       ISNULL(employee.CompanyId, 0) AS CompanyId
FROM ApprovalEffectJobs job
INNER JOIN pending ON pending.RequestId=job.RequestId
INNER JOIN SelfServiceRequests request ON request.Id=job.RequestId
INNER JOIN Employees employee ON employee.Id=request.EmployeeId;
""", null, reader => new PendingJob(
            HrmsDatabase.GetInt(reader, "RequestId"),
            HrmsDatabase.GetString(reader, "Actor"),
            HrmsDatabase.GetString(reader, "IpAddress"),
            HrmsDatabase.GetInt(reader, "CompanyId")));
    }
}

/// <summary>يعيد محاولة آثار الاعتمادات التي تعثرت من دون إعادة اعتماد الطلب.</summary>
public sealed class ApprovalEffectDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<ApprovalEffectDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var jobs = await ApprovalEffectJobStore.ClaimPendingAsync(db);

            foreach (var job in jobs)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (job.CompanyId <= 0)
                {
                    logger.LogError(
                        "Approval effect job {RequestId} has no valid company and will be retried.",
                        job.RequestId);
                    continue;
                }

                try
                {
                    await ApprovalEffectJobStore.ApplyNowAsync(
                        db,
                        job.RequestId,
                        CompanyScope.ForCompanies([job.CompanyId]),
                        job.Actor,
                        job.IpAddress);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Approval effects failed for request {RequestId}; a later run will retry.",
                        job.RequestId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Approval effect dispatcher failed; the next interval will retry.");
        }
    }
}
