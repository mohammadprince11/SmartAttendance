using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class OnboardingProtectedAssetValidationTests
{
    [Fact]
    public async Task OversizedFile_IsRejectedBeforeDatabaseAndThreatScan()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options);

        var scanner = new RecordingScanner();
        var root = Path.Combine(
            Path.GetTempPath(),
            "zynora-asset-validation-" + Guid.NewGuid().ToString("N"));
        var environment = new TestWebHostEnvironment(root);

        var service = new OnboardingProtectedAssetService(
            db,
            environment,
            scanner,
            Options.Create(new MalwareScanningOptions
            {
                Enabled = true,
                Required = true,
                Host = "unused",
                Port = 3310
            }),
            NullLogger<OnboardingProtectedAssetService>.Instance);

        var result = await service.SaveAsync(
            new OversizedFormFile(),
            companyId: 1,
            sessionId: 1,
            category: "NationalId",
            createdBySystemUserId: null);

        Assert.Null(result.Asset);
        Assert.Equal(
            ProtectedOnboardingAssetErrorCodes.FileTooLarge,
            result.ErrorCode);
        Assert.False(scanner.WasCalled);
        Assert.False(Directory.Exists(root));
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

    private sealed class OversizedFormFile : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => ProtectedFileStore.MaxFileSizeBytes + 1;
        public string Name => "file";
        public string FileName => "oversized.png";

        public void CopyTo(Stream target) =>
            throw new InvalidOperationException("Oversized file must be rejected before reading.");

        public Task CopyToAsync(
            Stream target,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Oversized file must be rejected before reading.");

        public Stream OpenReadStream() =>
            throw new InvalidOperationException("Oversized file must be rejected before reading.");
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
