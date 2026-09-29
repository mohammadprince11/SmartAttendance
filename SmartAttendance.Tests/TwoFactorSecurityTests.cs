using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class TwoFactorSecurityTests
{
    [Fact]
    public void Totp_ValidatesKnownRfc6238Vector()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var at59Seconds = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.True(TotpSecurity.ValidateCode(
            secret,
            "287082",
            at59Seconds));

        Assert.False(TotpSecurity.ValidateCode(
            secret,
            "287083",
            at59Seconds));
    }

    [Fact]
    public void Totp_AllowsOnlyOneThirtySecondDriftWindow()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        const string codeAt59Seconds = "287082";

        Assert.True(TotpSecurity.ValidateCode(
            secret,
            codeAt59Seconds,
            DateTimeOffset.FromUnixTimeSeconds(89)));

        Assert.False(TotpSecurity.ValidateCode(
            secret,
            codeAt59Seconds,
            DateTimeOffset.FromUnixTimeSeconds(90)));
    }

    [Fact]
    public void RecoveryCodeHash_IsCaseAndSeparatorInsensitive()
    {
        var a = TotpSecurity.HashRecoveryCode("ABCD-EFGH-JK234");
        var b = TotpSecurity.HashRecoveryCode("abcd efgh jk234");

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);

        var codes = TotpSecurity.GenerateRecoveryCodes(8);
        Assert.Equal(8, codes.Count);
        Assert.Equal(8, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TwoFactorContracts_AreWiredAcrossBackendAndMobile()
    {
        var root = FindRepoRoot();
        var auth = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Controllers", "Api", "AuthController.cs"));
        var controller = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Controllers", "Api", "TwoFactorController.cs"));
        var mobileApi = File.ReadAllText(Path.Combine(
            root, "ZynoraHR.Mobile", "MobileApi.cs"));
        var mobilePage = File.ReadAllText(Path.Combine(
            root, "ZynoraHR.Mobile", "MainPage.xaml.cs"));

        Assert.Contains("string? TwoFactorCode = null", auth, StringComparison.Ordinal);
        Assert.Contains("string? RecoveryCode = null", auth, StringComparison.Ordinal);
        Assert.Contains("requiresTwoFactor = true", auth, StringComparison.Ordinal);
        Assert.Contains("AppLoginTwoFactorStore.ConsumeRecoveryCodeAsync", auth, StringComparison.Ordinal);
        Assert.Contains("[Route(\"api/v1/auth/2fa\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"status\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"setup\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"enable\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"disable\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"recovery-codes\")]", controller, StringComparison.Ordinal);
        Assert.Contains("BeginTwoFactorSetupAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("EnableTwoFactorAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("TwoFactorLoginPanel.IsVisible = true", mobilePage, StringComparison.Ordinal);
        Assert.Contains("BuildTwoFactorServiceAsync", mobilePage, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task RecoveryCode_IsConsumedOnlyOnce()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SMARTATTENDANCE_2FA_TEST_CONNECTION") ??
            "Server=localhost;Database=SmartAttendance_E2E_UCA;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options);

        try
        {
            await db.Database.OpenConnectionAsync();
        }
        catch
        {
            Skip.If(true, "SmartAttendance_E2E_UCA is not available locally.");
            return;
        }

        await using var userCommand = db.Database.GetDbConnection().CreateCommand();
        userCommand.CommandText = "SELECT TOP (1) Id FROM AppLoginUsers ORDER BY Id;";
        var scalar = await userCommand.ExecuteScalarAsync();
        Skip.If(scalar is null or DBNull, "No AppLoginUsers test account is available.");
        var loginUserId = Convert.ToInt32(scalar);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM AppLoginTwoFactor WHERE LoginUserId = {loginUserId};");
            const string recoveryCode = "ABCD-EFGH-JK234";
            await AppLoginTwoFactorStore.BeginSetupAsync(db, loginUserId, "test-protected-secret");
            await AppLoginTwoFactorStore.EnablePendingAsync(
                db,
                loginUserId,
                new[] { TotpSecurity.HashRecoveryCode(recoveryCode) });

            Assert.True(await AppLoginTwoFactorStore.ConsumeRecoveryCodeAsync(db, loginUserId, recoveryCode));
            Assert.False(await AppLoginTwoFactorStore.ConsumeRecoveryCodeAsync(db, loginUserId, recoveryCode));
            Assert.Empty((await AppLoginTwoFactorStore.GetAsync(db, loginUserId)).RecoveryCodeHashes);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM AppLoginTwoFactor WHERE LoginUserId = {loginUserId};");
        }
    }

    [Fact]
    public void ControlledMigration_ContainsTwoFactorTable()
    {
        var migration = Assert.Single(
            SqlSchemaMigrator.Migrations,
            item => item.Id == "20260925-01-app-login-two-factor");

        Assert.Contains("CREATE TABLE AppLoginTwoFactor", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("PendingSecretProtected", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("RecoveryCodeHashesJson", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", migration.Sql, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
