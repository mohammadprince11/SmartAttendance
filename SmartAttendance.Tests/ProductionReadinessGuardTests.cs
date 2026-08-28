using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Observability;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Tests;

public sealed class ProductionReadinessGuardTests
{
    [Fact]
    public void Development_DoesNotRequireProductionOperations()
    {
        var failures = ProductionReadinessGuard.Validate(
            "Development",
            new ProductionOperationsOptions(),
            new SmtpOptions(),
            new MalwareScanningOptions());

        Assert.Empty(failures);
    }

    [Fact]
    public void Production_MissingControls_FailsClosedWithActionableFields()
    {
        var failures = ProductionReadinessGuard.Validate(
            "Production",
            new ProductionOperationsOptions(),
            new SmtpOptions(),
            new MalwareScanningOptions());

        Assert.Contains(failures, value => value.Contains("OwnerAcceptanceReference"));
        Assert.Contains(failures, value => value.Contains("RpoMinutes"));
        Assert.Contains(failures, value => value.Contains("RtoMinutes"));
        Assert.Contains(failures, value => value.Contains("OffsiteBackupPath"));
        Assert.Contains(failures, value => value.Contains("BackupHeartbeatPath"));
        Assert.Contains(failures, value => value.Contains("HealthMonitorUrl"));
        Assert.Contains(failures, value => value.Contains("AlertWebhookUrl"));
        Assert.Contains(failures, value => value.Contains("SMTP"));
        Assert.Contains(failures, value => value.Contains("malware"));
    }

    [Fact]
    public void Production_CompleteProfile_Passes()
    {
        var operations = new ProductionOperationsOptions
        {
            OwnerAcceptanceReference = "owner-signoff-2026-08-26",
            RpoMinutes = 60,
            RtoMinutes = 120,
            OffsiteBackupPath = @"\\backup-host\zynora",
            BackupHeartbeatPath = @"C:\Zynora\last-backup.json",
            HealthMonitorUrl = "https://hr.example.com/health/ready",
            AlertWebhookUrl = "https://alerts.example.com/zynora"
        };
        var smtp = new SmtpOptions
        {
            Enabled = true,
            Host = "smtp.example.com",
            FromAddress = "zynora@example.com"
        };
        var malware = new MalwareScanningOptions
        {
            Enabled = true,
            Required = true,
            Host = "clamav.internal",
            Port = 3310,
            TimeoutSeconds = 30
        };

        Assert.Empty(ProductionReadinessGuard.Validate(
            "Production", operations, smtp, malware));
    }

    [Fact]
    public void ExplicitEmergencyBypass_IsVisibleAndPassesGuard()
    {
        var operations = new ProductionOperationsOptions
        {
            EnforceProductionReadiness = false
        };

        Assert.Empty(ProductionReadinessGuard.Validate(
            "Production", operations, new SmtpOptions(), new MalwareScanningOptions()));
    }

    [Theory]
    [InlineData("stream: OK", FileThreatScanVerdict.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND", FileThreatScanVerdict.Threat)]
    [InlineData("stream: size limit exceeded. ERROR", FileThreatScanVerdict.Error)]
    [InlineData("", FileThreatScanVerdict.Error)]
    public void ClamAvResponse_IsParsedFailClosed(
        string response,
        FileThreatScanVerdict expected)
    {
        Assert.Equal(expected, ClamAvFileThreatScanner.ParseResponse(response).Verdict);
    }

    [Fact]
    public void RequiredScanner_RejectsUnavailable_OptionalScannerMayProceed()
    {
        var unavailable = new FileThreatScanResult(FileThreatScanVerdict.Unavailable);

        Assert.False(FileThreatPolicy.CanStore(
            new MalwareScanningOptions { Required = true }, unavailable));
        Assert.True(FileThreatPolicy.CanStore(
            new MalwareScanningOptions { Required = false }, unavailable));
        Assert.False(FileThreatPolicy.CanStore(
            new MalwareScanningOptions { Required = false },
            new FileThreatScanResult(FileThreatScanVerdict.Error)));
        Assert.False(FileThreatPolicy.CanStore(
            new MalwareScanningOptions { Required = false },
            new FileThreatScanResult(FileThreatScanVerdict.Threat)));
    }

    [Fact]
    public async Task BackupHealth_RequiresFreshVerifiedOffsiteEvidence()
    {
        var heartbeatPath = Path.Combine(
            Path.GetTempPath(), $"zynora-backup-heartbeat-{Guid.NewGuid():N}.json");

        try
        {
            var options = new ProductionOperationsOptions
            {
                EnforceProductionReadiness = true,
                RpoMinutes = 60,
                BackupHeartbeatPath = heartbeatPath
            };
            var check = new BackupFreshnessHealthCheck(
                Options.Create(options), new TestEnvironment("Production"));

            Assert.Equal(
                HealthStatus.Unhealthy,
                (await check.CheckHealthAsync(new HealthCheckContext())).Status);

            await File.WriteAllTextAsync(heartbeatPath, $$"""
                {
                  "completedAtUtc": "{{DateTimeOffset.UtcNow:o}}",
                  "verified": true,
                  "offsiteCopied": true
                }
                """);

            Assert.Equal(
                HealthStatus.Healthy,
                (await check.CheckHealthAsync(new HealthCheckContext())).Status);

            await File.WriteAllTextAsync(heartbeatPath, $$"""
                {
                  "completedAtUtc": "{{DateTimeOffset.UtcNow.AddHours(-2):o}}",
                  "verified": true,
                  "offsiteCopied": true
                }
                """);

            Assert.Equal(
                HealthStatus.Unhealthy,
                (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        }
        finally
        {
            if (File.Exists(heartbeatPath)) File.Delete(heartbeatPath);
        }
    }

    [Fact]
    public void EveryProtectedFileWrite_HasThreatScanInItsOwningPath()
    {
        var root = RepoRoot();
        var web = Path.Combine(root, "SmartAttendance.Web");
        var writers = Directory.GetFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ProtectedFileStore.SaveAsync", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(writers);
        foreach (var writer in writers)
        {
            Assert.True(
                File.ReadAllText(writer).Contains("ScanUploadAsync", StringComparison.Ordinal),
                $"مسار كتابة ملف محمي بلا فحص malware: {writer}");
        }
    }

    [Fact]
    public void OperationsScripts_KeepVerificationOffsiteAlertingAndGuardedRestore()
    {
        var root = RepoRoot();
        var backup = File.ReadAllText(Path.Combine(
            root, "scripts", "operations", "Backup-Zynora.ps1"));
        var restore = File.ReadAllText(Path.Combine(
            root, "scripts", "operations", "Test-ZynoraRestore.ps1"));
        var monitor = File.ReadAllText(Path.Combine(
            root, "scripts", "operations", "Monitor-Zynora.ps1"));
        var deploy = File.ReadAllText(Path.Combine(
            root, "scripts", "deploy", "Publish-Zynora.ps1"));

        Assert.Contains("RESTORE VERIFYONLY", backup);
        Assert.Contains("Get-FileHash", backup);
        Assert.Contains("OffsiteBackupRoot", backup);
        Assert.Contains("SmartAttendance_RestoreDrill_", restore);
        Assert.Contains("finally", restore);
        Assert.Contains("DROP DATABASE", restore);
        Assert.Contains("AlertWebhookUrl.Scheme -ne 'https'", monitor);
        Assert.Contains("'recovered'", monitor);
        Assert.Contains("EnforceProductionReadiness", deploy);
        Assert.Contains("MalwareScanning:Required=true", deploy);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmartAttendance.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
