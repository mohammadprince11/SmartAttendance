using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Integrations;

/// <summary>يحوّل inbox الخام إلى AttendanceRecords، مع retry وdead-letter.</summary>
public sealed class DevicePunchProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevicePunchProcessorService> _logger;

    public DevicePunchProcessorService(
        IServiceScopeFactory scopeFactory, ILogger<DevicePunchProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await SqlDistributedLock.TryRunAsync(db, "ZYNORA.DevicePunchProcessor",
                    () => ProcessBatchAsync(db, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Device punch processing cycle failed.");
            }
        }
    }

    public static async Task<(int Processed, int Retried, int DeadLetter)> ProcessBatchAsync(
        ApplicationDbContext db, CancellationToken cancellationToken, int batchSize = 200)
    {
        var items = await ClaimAsync(db, batchSize, cancellationToken);
        var processed = 0;
        var retried = 0;
        var dead = 0;
        foreach (var item in items)
        {
            var employeeId = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT TOP 1 Id FROM Employees
WHERE EmployeeNo=@No AND CompanyId=@CompanyId AND ISNULL(IsDeleted,0)=0
ORDER BY IsActive DESC,Id;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@No", item.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@CompanyId", item.CompanyId);
            });

            if (employeeId <= 0)
            {
                var terminal = item.AttemptCount >= 5;
                await FailAsync(db, item.Id, item.AttemptCount, terminal,
                    "Employee number was not found in the connector company.");
                if (terminal) dead++; else retried++;
                continue;
            }

            try
            {
                await HrmsDatabase.ExecuteAsync(db, """
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
INSERT INTO AttendanceRecords
    (EmployeeId,AttendanceDate,CheckIn,CheckOut,Source,Status,Notes,CreatedAt,IsDeleted)
VALUES
    (@EmployeeId,CAST(@PunchAt AS date),CONVERT(datetime2,@PunchAt),
     CASE WHEN @PunchType=N'Out' THEN CONVERT(datetime2,@PunchAt) ELSE NULL END,
     1,1,@Notes,SYSUTCDATETIME(),0);
UPDATE DevicePunchInbox
SET Status=N'Processed',ProcessedAt=SYSUTCDATETIME(),LastError=NULL
WHERE Id=@Id AND Status=N'Processing';
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@PunchAt", item.PunchedAt);
                    HrmsDatabase.AddParameter(command, "@PunchType", item.PunchType ?? "Unknown");
                    HrmsDatabase.AddParameter(command, "@Notes",
                        $"Device connector {item.ConnectorKey} | ExternalId {item.ExternalId}");
                    HrmsDatabase.AddParameter(command, "@Id", item.Id);
                });
                processed++;
            }
            catch (Exception exception)
            {
                var terminal = item.AttemptCount >= 5;
                await FailAsync(db, item.Id, item.AttemptCount, terminal, exception.Message);
                if (terminal) dead++; else retried++;
                continue;
            }
        }
        return (processed, retried, dead);
    }

    private static async Task<List<InboxItem>> ClaimAsync(
        ApplicationDbContext db, int batchSize, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await HrmsDatabase.QueryAsync(db, """
;WITH due AS
(
    SELECT TOP (@Batch) Id FROM DevicePunchInbox WITH (UPDLOCK,READPAST,ROWLOCK)
    WHERE (Status IN (N'Pending',N'Retry') AND NextAttemptAt<=SYSUTCDATETIME())
       OR (Status=N'Processing' AND NextAttemptAt<DATEADD(minute,-5,SYSUTCDATETIME()))
    ORDER BY CreatedAt,Id
)
UPDATE i SET Status=N'Processing',AttemptCount=AttemptCount+1,NextAttemptAt=SYSUTCDATETIME()
OUTPUT inserted.Id,inserted.CompanyId,inserted.ConnectorKey,inserted.ExternalId,
       inserted.EmployeeNo,inserted.PunchedAt,inserted.PunchType,inserted.AttemptCount
FROM DevicePunchInbox i INNER JOIN due ON due.Id=i.Id;
""", command => HrmsDatabase.AddParameter(command, "@Batch", Math.Clamp(batchSize, 1, 1000)), Map);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private static Task FailAsync(
        ApplicationDbContext db, long id, int attempt, bool terminal, string error) =>
        HrmsDatabase.ExecuteAsync(db, """
UPDATE DevicePunchInbox
SET Status=@Status,LastError=LEFT(@Error,1000),
    NextAttemptAt=DATEADD(second,@Delay,SYSUTCDATETIME())
WHERE Id=@Id AND Status=N'Processing';
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@Status", terminal ? "DeadLetter" : "Retry");
            HrmsDatabase.AddParameter(command, "@Error", error);
            HrmsDatabase.AddParameter(command, "@Delay", Math.Min(3600, 30 * (int)Math.Pow(2, Math.Min(attempt - 1, 7))));
        });

    private static InboxItem Map(DbDataReader reader) => new(
        Convert.ToInt64(reader["Id"]), HrmsDatabase.GetInt(reader, "CompanyId"),
        HrmsDatabase.GetString(reader, "ConnectorKey"), HrmsDatabase.GetString(reader, "ExternalId"),
        HrmsDatabase.GetString(reader, "EmployeeNo"), (DateTimeOffset)reader["PunchedAt"],
        HrmsDatabase.GetString(reader, "PunchType") is { Length: > 0 } type ? type : null,
        HrmsDatabase.GetInt(reader, "AttemptCount"));

    private sealed record InboxItem(
        long Id, int CompanyId, string ConnectorKey, string ExternalId,
        string EmployeeNo, DateTimeOffset PunchedAt, string? PunchType, int AttemptCount);
}
