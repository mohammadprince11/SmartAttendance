using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Controllers.Api;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class TwoFactorLoginBehaviorTests
{
    private const string Username = "__2fa_login_contract__";
    private const string Password = "Test!Pass-2026";
    private const string RecoveryCode = "ABCD-EFGH-JK234";
    private const int SystemUserId = 990001;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SMARTATTENDANCE_2FA_TEST_CONNECTION") ??
        "Server=localhost;Database=SmartAttendance_E2E_UCA;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";
    [SkippableFact]
    public async Task Login_RequiresSecondFactor_AndRecoveryCodeIsOneTime()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString)
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

        var migration = Assert.Single(
            SqlSchemaMigrator.Migrations,
            item => item.Id == "20260925-01-app-login-two-factor");
        await db.Database.ExecuteSqlRawAsync(migration.Sql);

        await CleanupAsync(db);
        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(Password, salt);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO AppLoginUsers
    (EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive, FailedLoginAttempts, SecurityStamp, CreatedAt)
VALUES
    (NULL, {Username}, {hash}, {salt}, 'Employee', 1, 0, 'TEST-2FA-STAMP', SYSUTCDATETIME());
""");

            var user = await LoginDatabase.GetByUsernameAsync(db, Username);
            Assert.NotNull(user);

            await AppLoginTwoFactorStore.BeginSetupAsync(
                db,
                user!.Id,
                "test-protected-secret");
            await AppLoginTwoFactorStore.EnablePendingAsync(
                db,
                user.Id,
                new[] { TotpSecurity.HashRecoveryCode(RecoveryCode) });

            using var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = new AuthController(
                db,
                new FakeLoginIdentityService(),
                cache,
                new EphemeralDataProtectionProvider());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            var missing = await controller.Login(
                new AuthController.LoginRequest(Username, Password));
            AssertRequiresTwoFactor(missing);

            var invalid = await controller.Login(
                new AuthController.LoginRequest(
                    Username,
                    Password,
                    RecoveryCode: "WRONG-RECOVERY"));
            AssertRequiresTwoFactor(invalid);

            var valid = await controller.Login(
                new AuthController.LoginRequest(
                    Username,
                    Password,
                    RecoveryCode: RecoveryCode));
            var ok = Assert.IsType<OkObjectResult>(valid);
            using var okJson = JsonDocument.Parse(
                JsonSerializer.Serialize(ok.Value));
            var token = okJson.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            var reused = await controller.Login(
                new AuthController.LoginRequest(
                    Username,
                    Password,
                    RecoveryCode: RecoveryCode));
            AssertRequiresTwoFactor(reused);
        }
        finally
        {
            await CleanupAsync(db);
        }
    }

    private static void AssertRequiresTwoFactor(IActionResult result)
    {
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(unauthorized.Value));

        Assert.True(
            json.RootElement
                .GetProperty("requiresTwoFactor")
                .GetBoolean());
        Assert.False(
            json.RootElement.TryGetProperty("token", out _));
    }

    private static async Task CleanupAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
IF OBJECT_ID('ApiTokens', 'U') IS NOT NULL
    DELETE FROM ApiTokens WHERE Username = {Username};
IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
    DELETE FROM AuditLogs WHERE UserName = {Username};
IF OBJECT_ID('AppLoginTwoFactor', 'U') IS NOT NULL
    DELETE FROM AppLoginTwoFactor
    WHERE LoginUserId IN (
        SELECT Id FROM AppLoginUsers WHERE Username = {Username});
DELETE FROM AppLoginUsers WHERE Username = {Username};
""");
    }

    private sealed class FakeLoginIdentityService : ILoginIdentityService
    {
        public Task<int?> EnsureSystemUserAsync(
            LoginIdentityRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(SystemUserId);
    }
}
