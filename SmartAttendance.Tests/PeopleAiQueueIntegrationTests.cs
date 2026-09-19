using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiQueueIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;
    private readonly List<long> _sessions = [];

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await PeopleAiSettingsStore.EnsureDefaultsAsync(_db);
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }
    public async Task DisposeAsync()
    {
        if (_dbAvailable)
        {
            foreach (var sessionId in _sessions)
            {
                await HrmsDatabase.ExecuteAsync(
                    _db,
                    """
                    DELETE FROM dbo.PeopleAiJobs WHERE SessionId = @SessionId;
                    DELETE FROM dbo.OnboardingDocuments WHERE SessionId = @SessionId;
                    DELETE FROM dbo.ProtectedFileAssets
                    WHERE OwnerType = 'OnboardingSession'
                      AND OwnerId = @SessionId;
                    DELETE FROM dbo.EmployeeOnboardingSessions
                    WHERE Id = @SessionId;
                    """,
                    command => HrmsDatabase.AddParameter(
                        command, "@SessionId", sessionId));
            }
        }

        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Retry_StopsAtConfiguredMaxAttempts()
    {
        Skip.IfNot(_dbAvailable, "Disposable SQL is unavailable.");
        var (_, documentId, _) = await CreateQueuedDocumentAsync();

        var job = await EmployeeOnboardingStore.ClaimNextJobAsync(
            _db, "itest-worker-1");
        Assert.NotNull(job);
        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.PeopleAiJobs
            SET MaxAttempts = 2
            WHERE Id = @JobId;
            """,
            command => HrmsDatabase.AddParameter(
                command, "@JobId", job!.Id));

        await EmployeeOnboardingStore.FailJobAsync(
            _db, job.Id, "ITEST_FAIL_1", TimeSpan.Zero);

        var retry = await EmployeeOnboardingStore.ClaimNextJobAsync(
            _db, "itest-worker-2");
        Assert.NotNull(retry);
        Assert.Equal(job.Id, retry!.Id);
        Assert.Equal(2, retry.AttemptCount);

        await EmployeeOnboardingStore.FailJobAsync(
            _db, retry.Id, "ITEST_FAIL_2", TimeSpan.Zero);

        var none = await EmployeeOnboardingStore.ClaimNextJobAsync(
            _db, "itest-worker-3");
        Assert.Null(none);

        var status = await ScalarStringAsync(
            "SELECT Status FROM dbo.PeopleAiJobs WHERE Id = @Id;",
            ("@Id", retry.Id));
        Assert.Equal("Failed", status);
        Assert.True(documentId > 0);
    }
    [SkippableFact]
    public async Task StaleWorker_RecoversThenBecomesTerminalAtMaxAttempts()
    {
        Skip.IfNot(_dbAvailable, "Disposable SQL is unavailable.");
        var (_, documentId, _) = await CreateQueuedDocumentAsync();

        var job = await EmployeeOnboardingStore.ClaimNextJobAsync(
            _db, "itest-stale");
        Assert.NotNull(job);

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.PeopleAiJobs
            SET LockedAt = DATEADD(minute, -10, SYSUTCDATETIME()),
                MaxAttempts = 2
            WHERE Id = @JobId;
            UPDATE dbo.OnboardingDocuments
            SET ProcessingStatus = 'Processing'
            WHERE Id = @DocumentId;
            """,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@JobId", job!.Id);
                HrmsDatabase.AddParameter(
                    command, "@DocumentId", documentId);
            });

        await EmployeeOnboardingStore.RecoverStaleJobsAsync(
            _db, TimeSpan.FromMinutes(1));
        Assert.Equal("Retry", await ScalarStringAsync(
            "SELECT Status FROM dbo.PeopleAiJobs WHERE Id = @Id;",
            ("@Id", job!.Id)));
        var retried = await EmployeeOnboardingStore.ClaimNextJobAsync(
            _db, "itest-stale-2");
        Assert.NotNull(retried);
        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.PeopleAiJobs
            SET LockedAt = DATEADD(minute, -10, SYSUTCDATETIME())
            WHERE Id = @JobId;
            UPDATE dbo.OnboardingDocuments
            SET ProcessingStatus = 'Processing'
            WHERE Id = @DocumentId;
            """,
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@JobId", retried!.Id);
                HrmsDatabase.AddParameter(
                    command, "@DocumentId", documentId);
            });

        await EmployeeOnboardingStore.RecoverStaleJobsAsync(
            _db, TimeSpan.FromMinutes(1));

        Assert.Equal("Failed", await ScalarStringAsync(
            "SELECT Status FROM dbo.PeopleAiJobs WHERE Id = @Id;",
            ("@Id", retried!.Id)));
        Assert.Equal("Failed", await ScalarStringAsync(
            "SELECT ProcessingStatus FROM dbo.OnboardingDocuments WHERE Id = @Id;",
            ("@Id", documentId)));
    }
    [SkippableFact]
    public async Task ManualRequeue_CreatesOnlyOneActiveRetryJob()
    {
        Skip.IfNot(_dbAvailable, "Disposable SQL is unavailable.");
        var (sessionId, documentId, jobId) =
            await CreateQueuedDocumentAsync();

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.PeopleAiJobs
            SET Status = 'Failed', AttemptCount = MaxAttempts
            WHERE Id = @JobId;
            UPDATE dbo.OnboardingDocuments
            SET ProcessingStatus = 'Failed',
                ProcessingErrorCode = 'ITEST'
            WHERE Id = @DocumentId;
            """,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@JobId", jobId);
                HrmsDatabase.AddParameter(
                    command, "@DocumentId", documentId);
            });

        Assert.True(await EmployeeOnboardingStore
            .RequeueFailedDocumentAsync(_db, sessionId, documentId));
        Assert.False(await EmployeeOnboardingStore
            .RequeueFailedDocumentAsync(_db, sessionId, documentId));

        var active = await ScalarIntAsync(
            """
            SELECT COUNT(*) FROM dbo.PeopleAiJobs
            WHERE OnboardingDocumentId = @DocumentId
              AND Status IN ('Queued', 'Retry', 'Processing');
            """,
            ("@DocumentId", documentId));
        Assert.Equal(1, active);
    }
    private async Task<(long SessionId, long DocumentId, long JobId)>
        CreateQueuedDocumentAsync()
    {
        var companyId = await ScalarIntAsync(
            "SELECT TOP 1 Id FROM dbo.Companies WHERE Code = 'E2E-A';");
        Skip.If(companyId <= 0, "E2E-A company is unavailable.");

        var sessionId = await EmployeeOnboardingStore.CreateSessionAsync(
            _db, companyId, null, TimeSpan.FromHours(1));
        _sessions.Add(sessionId);

        var assetId = await HrmsDatabase.ScalarAsync<long>(
            _db,
            """
            INSERT INTO dbo.ProtectedFileAssets
                (CompanyId, OwnerType, OwnerId, StorageKey,
                 OriginalFileName, MimeType, Extension, SizeBytes,
                 Sha256, SignatureValidationStatus, MalwareScanStatus)
            OUTPUT INSERTED.Id
            VALUES
                (@CompanyId, 'OnboardingSession', @SessionId, @StorageKey,
                 'itest.png', 'image/png', '.png', 68,
                 REPLICATE('a', 64), 'Valid', 'Clean');
            """,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@StorageKey",
                    $"itest/{Guid.NewGuid():N}.png");
            });

        var documentId =
            await EmployeeOnboardingStore.AddDocumentAndQueueAsync(
                _db, sessionId, assetId, "NationalId");
        var jobId = await HrmsDatabase.ScalarAsync<long>(
            _db,
            """
            SELECT TOP 1 Id
            FROM dbo.PeopleAiJobs
            WHERE OnboardingDocumentId = @DocumentId
            ORDER BY Id DESC;
            """,
            command => HrmsDatabase.AddParameter(
                command, "@DocumentId", documentId));

        return (sessionId, documentId, jobId);
    }

    private async Task<int> ScalarIntAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var value = await ScalarAsync(sql, parameters);
        return Convert.ToInt32(value);
    }

    private async Task<string> ScalarStringAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var value = await ScalarAsync(sql, parameters);
        return Convert.ToString(value) ?? string.Empty;
    }

    private async Task<object?> ScalarAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = parameter.Name;
            p.Value = parameter.Value;
            command.Parameters.Add(p);
        }

        return await command.ExecuteScalarAsync();
    }
}
