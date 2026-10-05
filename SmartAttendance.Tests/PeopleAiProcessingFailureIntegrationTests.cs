using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiProcessingFailureIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;
    private int _companyId;
    private readonly List<long> _sessions = [];
    private readonly string _contentRoot =
        Path.Combine(
            Path.GetTempPath(),
            "zynora-peopleai-processing-" + Guid.NewGuid().ToString("N"));

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await PeopleAiSettingsStore.EnsureDefaultsAsync(_db);
            _companyId = await ScalarIntAsync(
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-A';");
            _dbAvailable = _companyId > 0;
            Directory.CreateDirectory(_contentRoot);
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
                    DELETE FROM dbo.PeopleAiAuditLogs
                    WHERE SessionId=@SessionId;

                    DELETE f
                    FROM dbo.DocumentExtractedFields f
                    JOIN dbo.DocumentExtractionRuns r
                      ON r.Id=f.ExtractionRunId
                    JOIN dbo.OnboardingDocuments d
                      ON d.Id=r.OnboardingDocumentId
                    WHERE d.SessionId=@SessionId;

                    DELETE r
                    FROM dbo.DocumentExtractionRuns r
                    JOIN dbo.OnboardingDocuments d
                      ON d.Id=r.OnboardingDocumentId
                    WHERE d.SessionId=@SessionId;

                    DELETE FROM dbo.PeopleAiJobs
                    WHERE SessionId=@SessionId;

                    DELETE FROM dbo.OnboardingDocuments
                    WHERE SessionId=@SessionId;

                    DELETE FROM dbo.ProtectedFileAssets
                    WHERE OwnerType='OnboardingSession'
                      AND OwnerId=@SessionId;

                    DELETE FROM dbo.EmployeeOnboardingSessions
                    WHERE Id=@SessionId;
                    """,
                    command => HrmsDatabase.AddParameter(
                        command, "@SessionId", sessionId));
            }
        }

        await _db.DisposeAsync();

        try
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
        catch
        {
            // Test temp cleanup is best effort.
        }
    }
    [SkippableFact]
    public Task CorruptedFile_OcrError_IsPersistedAsTerminalFailure() =>
        RunFailureAsync(
            FailureMode.ResponseError,
            "CORRUPT_IMAGE");

    [SkippableFact]
    public Task OcrEngineFailure_IsPersistedAsTerminalFailure() =>
        RunFailureAsync(
            FailureMode.ResponseError,
            "OCR_ENGINE_FAILED");

    [SkippableFact]
    public Task ProcessingTimeout_IsPersistedAsTerminalFailure() =>
        RunFailureAsync(
            FailureMode.Timeout,
            "LOCAL_OCR_TIMEOUT");

    private async Task RunFailureAsync(
        FailureMode mode,
        string expectedErrorCode)
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");

        var sessionId = await EmployeeOnboardingStore.CreateSessionAsync(
            _db, _companyId, null, TimeSpan.FromHours(1));
        _sessions.Add(sessionId);

        var storageKey =
            $"company-{_companyId}/onboarding-{sessionId}/NationalId_{Guid.NewGuid():N}.png";

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
                 'failure.png', 'image/png', '.png', 16,
                 REPLICATE('c', 64), 'Valid', 'Clean');
            """,
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", _companyId);
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@StorageKey", storageKey);
            });
        var physicalPath = Path.Combine(
            OnboardingProtectedAssetService.ResolveRoot(_contentRoot),
            storageKey.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(
            Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(
            physicalPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
             0x43, 0x4F, 0x52, 0x52, 0x55, 0x50, 0x54, 0x21]);

        var documentId =
            await EmployeeOnboardingStore.AddDocumentAndQueueAsync(
                _db, sessionId, assetId, "NationalId");

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.PeopleAiJobs
            SET MaxAttempts=1
            WHERE OnboardingDocumentId=@DocumentId;
            """,
            command => HrmsDatabase.AddParameter(
                command, "@DocumentId", documentId));

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(
            options => options.UseSqlServer(ConnectionString));
        await using var provider = services.BuildServiceProvider();

        var fakeOcr = new FakeOcrClient(mode, expectedErrorCode);
        var environment = new TestWebHostEnvironment(_contentRoot);
        var options = Options.Create(new PeopleAiWorkerOptions
        {
            Enabled = true,
            PythonExecutable = "fake-python",
            ScriptPath = "fake-worker.py",
            PollSeconds = 1,
            JobTimeoutSeconds = 30
        });

        using var service = new PeopleAiJobProcessorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fakeOcr,
            environment,
            options,
            NullLogger<PeopleAiJobProcessorService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var terminal = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                terminal = await ScalarIntAsync(
                    """
                    SELECT COUNT(*)
                    FROM dbo.PeopleAiJobs
                    WHERE OnboardingDocumentId=@DocumentId
                      AND Status='Failed'
                      AND LastErrorCode=@ErrorCode;
                    """,
                    ("@DocumentId", documentId),
                    ("@ErrorCode", expectedErrorCode)) == 1;

                if (terminal)
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.True(
                terminal,
                $"Expected terminal People AI failure {expectedErrorCode}.");

            Assert.Equal(1, await ScalarIntAsync(
                """
                SELECT COUNT(*)
                FROM dbo.OnboardingDocuments
                WHERE Id=@DocumentId
                  AND ProcessingStatus='Failed'
                  AND ProcessingErrorCode=@ErrorCode;
                """,
                ("@DocumentId", documentId),
                ("@ErrorCode", expectedErrorCode)));

            Assert.Equal(1, await ScalarIntAsync(
                """
                SELECT COUNT(*)
                FROM dbo.PeopleAiAuditLogs
                WHERE SessionId=@SessionId
                  AND Operation='DocumentProcessed'
                  AND Success=0
                  AND ErrorCode=@ErrorCode;
                """,
                ("@SessionId", sessionId),
                ("@ErrorCode", expectedErrorCode)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
    private async Task<int> ScalarIntAsync(
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

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private enum FailureMode
    {
        ResponseError,
        Timeout
    }

    private sealed class FakeOcrClient(
        FailureMode mode,
        string errorCode) : ILocalOcrProcessClient
    {
        public bool IsEnabled => true;

        public Task EnsureReadyAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LocalOcrResponse> ExtractAsync(
            string physicalPath,
            CancellationToken cancellationToken = default)
        {
            if (mode == FailureMode.Timeout)
            {
                throw new TimeoutException(
                    "Synthetic People AI processing timeout.");
            }

            return Task.FromResult(new LocalOcrResponse(
                false,
                null,
                "E2E-FakeOCR",
                "FailureFixture",
                null,
                [],
                string.Empty,
                0,
                errorCode,
                "Synthetic failure."));
        }
    }
    private sealed class TestWebHostEnvironment(
        string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } =
            "SmartAttendance.Tests";

        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();

        public string WebRootPath { get; set; } =
            contentRootPath;

        public string EnvironmentName { get; set; } =
            "Testing";

        public string ContentRootPath { get; set; } =
            contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
