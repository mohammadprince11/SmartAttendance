using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Observability;

/// <summary>
/// يحوّل نجاح النسخ الخارجي من وعدٍ تشغيلي إلى readiness signal: ملف النبضة لا
/// يُكتب إلا بعد VERIFYONLY ومطابقة SHA-256، ويجب ألا يتجاوز عمره RPO المعتمد.
/// </summary>
public sealed class BackupFreshnessHealthCheck : IHealthCheck
{
    private readonly ProductionOperationsOptions _options;
    private readonly IWebHostEnvironment _environment;

    public BackupFreshnessHealthCheck(
        IOptions<ProductionOperationsOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsProduction() || !_options.EnforceProductionReadiness)
            return HealthCheckResult.Healthy("Production backup evidence is not enforced.");

        if (_options.RpoMinutes <= 0 || string.IsNullOrWhiteSpace(_options.BackupHeartbeatPath))
            return HealthCheckResult.Unhealthy("Backup readiness configuration is incomplete.");

        try
        {
            if (!File.Exists(_options.BackupHeartbeatPath))
                return HealthCheckResult.Unhealthy("No verified backup heartbeat exists.");

            await using var stream = File.OpenRead(_options.BackupHeartbeatPath);
            var heartbeat = await JsonSerializer.DeserializeAsync<BackupHeartbeat>(
                stream,
                cancellationToken: cancellationToken);

            if (heartbeat is null || !heartbeat.Verified || !heartbeat.OffsiteCopied)
                return HealthCheckResult.Unhealthy("Latest backup lacks verification/offsite evidence.");

            if (!DateTimeOffset.TryParse(heartbeat.CompletedAtUtc, out var completed))
                return HealthCheckResult.Unhealthy("Backup heartbeat timestamp is invalid.");

            var age = DateTimeOffset.UtcNow - completed.ToUniversalTime();
            if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(_options.RpoMinutes))
                return HealthCheckResult.Unhealthy(
                    $"Latest verified offsite backup is outside RPO ({Math.Max(0, age.TotalMinutes):N0} minutes old).",
                    data: new Dictionary<string, object>
                    {
                        ["ageMinutes"] = Math.Max(0, age.TotalMinutes),
                        ["rpoMinutes"] = _options.RpoMinutes
                    });

            return HealthCheckResult.Healthy(
                "Verified offsite backup is within RPO.",
                new Dictionary<string, object>
                {
                    ["ageMinutes"] = Math.Max(0, age.TotalMinutes),
                    ["rpoMinutes"] = _options.RpoMinutes
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return HealthCheckResult.Unhealthy("Backup heartbeat cannot be validated.", ex);
        }
    }

    private sealed class BackupHeartbeat
    {
        [JsonPropertyName("completedAtUtc")]
        public string? CompletedAtUtc { get; set; }

        [JsonPropertyName("verified")]
        public bool Verified { get; set; }

        [JsonPropertyName("offsiteCopied")]
        public bool OffsiteCopied { get; set; }
    }
}
