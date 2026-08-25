using System.Net;
using SmartAttendance.Web.Infrastructure.Integrations;

namespace SmartAttendance.Tests;

public sealed class WebhookIntegrationTests
{
    [Fact]
    public void Signature_IsDeterministicAndRejectsPayloadTimestampOrSecretChanges()
    {
        const string payload = """{"employeeId":42,"status":"approved"}""";
        const long timestamp = 1_800_000_000;
        var signature = WebhookSignature.Sign("tenant-secret", timestamp, payload);

        Assert.StartsWith("sha256=", signature, StringComparison.Ordinal);
        Assert.Equal(71, signature.Length);
        Assert.True(WebhookSignature.Verify("tenant-secret", timestamp, payload, signature));
        Assert.False(WebhookSignature.Verify("wrong", timestamp, payload, signature));
        Assert.False(WebhookSignature.Verify("tenant-secret", timestamp + 1, payload, signature));
        Assert.False(WebhookSignature.Verify("tenant-secret", timestamp, payload + " ", signature));
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.1.2.3/hook")]
    [InlineData("https://172.16.0.1/hook")]
    [InlineData("https://192.168.1.1/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    public void EndpointPolicy_RejectsUnsafeTargets(string value) =>
        Assert.False(WebhookEndpointPolicy.IsAllowed(new Uri(value)));

    [Fact]
    public void EndpointPolicy_AllowsPublicHttpsAndRejectsPrivateIpv6()
    {
        Assert.True(WebhookEndpointPolicy.IsAllowed(new Uri("https://events.example.com/zynora")));
        Assert.False(WebhookEndpointPolicy.IsPublic(IPAddress.IPv6Loopback));
        Assert.False(WebhookEndpointPolicy.IsPublic(IPAddress.Parse("fd00::1")));
        Assert.False(WebhookEndpointPolicy.IsPublic(IPAddress.Parse("fe80::1")));
        Assert.False(WebhookEndpointPolicy.IsPublic(IPAddress.Parse("169.254.10.20")));
        Assert.True(WebhookEndpointPolicy.IsPublic(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void OutboxContract_HasTenantIdempotencyRetrySigningAndDistributedLock()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "WebhookStore.cs");
        var dispatcher = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "WebhookDispatcherService.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var program = Read(root, "SmartAttendance.Web", "Program.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Integrations", "Webhooks.cshtml.cs");
        var payroll = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs");

        Assert.Contains("20260826-05-webhook-outbox", migration, StringComparison.Ordinal);
        Assert.Contains("UQ_WebhookDeliveries_Idempotency", migration, StringComparison.Ordinal);
        Assert.Contains("s.CompanyId=@CompanyId", store, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, READPAST, ROWLOCK", store, StringComparison.Ordinal);
        Assert.Contains("DeadLetter", store, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", dispatcher, StringComparison.Ordinal);
        Assert.Contains("X-Zynora-Signature", dispatcher, StringComparison.Ordinal);
        Assert.Contains("SqlDistributedLock.TryRunAsync", dispatcher, StringComparison.Ordinal);
        Assert.Contains("AllowAutoRedirect = false", program, StringComparison.Ordinal);
        Assert.Contains("scope.Allows(companyId)", page, StringComparison.Ordinal);
        Assert.Contains("RandomNumberGenerator.GetBytes(32)", page, StringComparison.Ordinal);
        Assert.Contains("\"payroll.issued\"", payroll, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", payroll, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
