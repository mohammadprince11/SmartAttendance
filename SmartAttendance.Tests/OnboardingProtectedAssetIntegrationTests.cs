using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class OnboardingProtectedAssetIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;
    private int _companyId;
    private long _sessionId;
    private readonly string _contentRoot =
        Path.Combine(
            Path.GetTempPath(),
            "zynora-onboarding-asset-" + Guid.NewGuid().ToString("N"));

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString).Options);
    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await PeopleAiSettingsStore.EnsureDefaultsAsync(_db);
            _companyId = await HrmsDatabase.ScalarAsync<int>(
                _db,
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-A';");

            if (_companyId > 0)
            {
                _sessionId =
                    await EmployeeOnboardingStore.CreateSessionAsync(
                        _db,
                        _companyId,
                        null,
                        TimeSpan.FromHours(1));
            }

            _dbAvailable = _companyId > 0 && _sessionId > 0;
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
            await HrmsDatabase.ExecuteAsync(
                _db,
                """
                DELETE FROM dbo.ProtectedFileAssets
                WHERE OwnerType='OnboardingSession'
                  AND OwnerId=@SessionId;
                DELETE FROM dbo.EmployeeOnboardingSessions
                WHERE Id=@SessionId;
                """,
                command => HrmsDatabase.AddParameter(
                    command, "@SessionId", _sessionId));
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
        }
    }
    [SkippableFact]
    public async Task InvalidExtension_IsRejectedBeforeStorageAndScanner()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");

        var scanner = new RecordingScanner();
        var service = new OnboardingProtectedAssetService(
            _db,
            new TestWebHostEnvironment(_contentRoot),
            scanner,
            Options.Create(new MalwareScanningOptions
            {
                Enabled = true,
                Required = true,
                Host = "unused",
                Port = 3310
            }),
            NullLogger<OnboardingProtectedAssetService>.Instance);

        var bytes = "MZ-synthetic-invalid-extension"u8.ToArray();
        await using var stream = new MemoryStream(bytes);
        var formFile = new FormFile(
            stream,
            0,
            bytes.Length,
            "file",
            "synthetic.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        var result = await service.SaveAsync(
            formFile,
            _companyId,
            _sessionId,
            "NationalId",
            null);

        Assert.Null(result.Asset);
        Assert.Equal(
            ProtectedOnboardingAssetErrorCodes.ExtensionNotAllowed,
            result.ErrorCode);
        Assert.False(scanner.WasCalled);

        var stored = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
            SELECT COUNT(*) FROM dbo.ProtectedFileAssets
            WHERE OwnerType='OnboardingSession'
              AND OwnerId=@SessionId;
            """,
            command => HrmsDatabase.AddParameter(
                command, "@SessionId", _sessionId));
        Assert.Equal(0, stored);
    }
    private sealed class RecordingScanner : IFileThreatScanner
    {
        public bool WasCalled { get; private set; }

        public Task<FileThreatScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(FileThreatScanResult.Clean);
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
