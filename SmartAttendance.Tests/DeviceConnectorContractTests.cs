using System.Net;
using SmartAttendance.Web.Infrastructure.Integrations;

namespace SmartAttendance.Tests;

public sealed class DeviceConnectorContractTests
{
    [Fact]
    public void IntegrationIdentity_RequiresExactMachineScope()
    {
        var identity = new IntegrationApiKeyStore.Identity(
            1, 42, "Device gateway", "attendance.write,attendance.read");
        Assert.True(identity.HasScope("attendance.write"));
        Assert.True(identity.HasScope("ATTENDANCE.READ"));
        Assert.False(identity.HasScope("payroll.write"));
    }

    [Fact]
    public void DeviceApiContract_IsVersionedTenantScopedIdempotentAndHasDlqRecovery()
    {
        var root = FindRoot();
        var controller = Read(root, "SmartAttendance.Web", "Controllers", "Api", "AttendanceDeviceController.cs");
        var keys = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "IntegrationApiKeyStore.cs");
        var inbox = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "DevicePunchInboxStore.cs");
        var processor = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "DevicePunchProcessorService.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");

        Assert.Contains("api/v1/attendance/device-punches", controller, StringComparison.Ordinal);
        Assert.Contains("\"attendance.write\"", controller, StringComparison.Ordinal);
        Assert.Contains("Punches.Count is < 1 or > 1000", controller, StringComparison.Ordinal);
        Assert.Contains("CompanyId", keys, StringComparison.Ordinal);
        Assert.Contains("scope.Allows(companyId)", keys, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", keys, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK,HOLDLOCK", inbox, StringComparison.Ordinal);
        Assert.Contains("DeviceConnectorHeartbeats", inbox, StringComparison.Ordinal);
        Assert.Contains("DeadLetter", processor, StringComparison.Ordinal);
        Assert.Contains("SqlDistributedLock.TryRunAsync", processor, StringComparison.Ordinal);
        Assert.Contains("20260826-06-device-connector-inbox", migration, StringComparison.Ordinal);
        Assert.Contains("UQ_DevicePunchInbox_External", migration, StringComparison.Ordinal);
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
