using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiWorkerRecoveryTests
{
    [Fact]
    public async Task StartupPreflight_RetriesAfterTransientOcrFailure()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(),
            "zynora-people-ai-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            await using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var ocr = new RecoveringOcrClient();
            var options = Options.Create(new PeopleAiWorkerOptions
            {
                Enabled = true,
                PythonExecutable = "synthetic-python",
                ScriptPath = "synthetic-worker.py",
                PollSeconds = 1,
                StartupRetrySeconds = 1
            });

            using var service = new PeopleAiJobProcessorService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                ocr,
                new TestWebHostEnvironment(contentRoot),
                options,
                NullLogger<PeopleAiJobProcessorService>.Instance);

            await service.StartAsync(CancellationToken.None);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (ocr.ReadinessAttempts < 2 &&
                       DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.True(
                    ocr.ReadinessAttempts >= 2,
                    "The queue supervisor did not retry OCR startup.");
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void WindowsWatchdog_EnablesThePackagedPeopleAiRuntime()
    {
        var root = FindRoot();
        var launcher = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "deploy",
            "Start-Zynora-Windows.ps1"));

        Assert.Contains("PeopleAIWorker__Enabled", launcher);
        Assert.Contains("PeopleAIWorker__PythonExecutable", launcher);
        Assert.Contains("PeopleAIWorker__TempDirectory", launcher);
        Assert.Contains("PeopleAIWorker__StartupRetrySeconds", launcher);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class RecoveringOcrClient : ILocalOcrProcessClient
    {
        private int _readinessAttempts;

        public bool IsEnabled => true;
        public int ReadinessAttempts => Volatile.Read(ref _readinessAttempts);

        public Task EnsureReadyAsync(
            CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _readinessAttempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException(
                    "Synthetic transient OCR startup failure.");
            }

            return Task.CompletedTask;
        }

        public Task<LocalOcrResponse> ExtractAsync(
            string physicalPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestWebHostEnvironment(
        string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } =
            "SmartAttendance.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
