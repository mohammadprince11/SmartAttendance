using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public sealed class EmployeePortalModernRequestMobileTests : PageTest
{
    private string BaseUrl =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")!.TrimEnd('/');

    private string? DatabaseName =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_DATABASE_NAME");

    private string Username =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_EMPLOYEE_USERNAME") ??
        (!string.IsNullOrWhiteSpace(DatabaseName)
            ? "employee"
            : Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME"))!;

    private string Password =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_EMPLOYEE_PASSWORD") ??
        (!string.IsNullOrWhiteSpace(DatabaseName)
            ? DisposableEmployeeCredential(DatabaseName!)
            : Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD"))!;

    private static string DisposableEmployeeCredential(string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "ZYNORA-E2E-EMPLOYEE-DISPOSABLE:" + value));
        return "E2E-" + Convert.ToHexString(bytes)[..24] + "-Aa1!";
    }

    [Test]
    public async Task ModernRequestSheet_Fits_360_390_430()
    {
        await LoginAsync();

        foreach (var width in new[] { 360, 390, 430 })
        {
            await Page.SetViewportSizeAsync(width, 900);
            await Page.GotoAsync($"{BaseUrl}/EmployeePortal?tab=requests&open=leave",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Page.Locator("#nxex-leave-modal.open").WaitForAsync(new() { Timeout = 10000 });
            var departures = Page.Locator("[data-leave-tab]").Filter(new() { HasText = "المغادرات" });
            await departures.ClickAsync();
            var form = Page.Locator("[data-leave-panel]:not([hidden])");
            var typeTrigger = form.Locator("[data-csel-trigger]");
            await typeTrigger.WaitForAsync();
            await typeTrigger.ClickAsync();
            await form.Locator("[data-csel-list]:not([hidden])").WaitForAsync();
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-csel-list]:not([hidden])", width);
            await typeTrigger.ClickAsync();

            var today = DateTime.Today.ToString("yyyy-MM-dd");
            await form.Locator("[data-dp-from]").EvaluateAsync("(el,v)=>{el.value=v;el.dispatchEvent(new Event('change',{bubbles:true}));}", today);
            await form.Locator("[data-dp-to]").EvaluateAsync("(el,v)=>{el.value=v;el.dispatchEvent(new Event('change',{bubbles:true}));}", today);
            await form.Locator("[data-punch-block]:not([hidden])").WaitForAsync(new() { Timeout = 10000 });

            await AssertFitsAsync("#nxex-leave-modal", width);
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-csel-trigger]", width);
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-punch-block]", width);

            await form.Locator("[data-open-datepicker]").ClickAsync();
            await Page.Locator("#nxex-dp-sheet.open").WaitForAsync();
            await AssertFitsAsync("#nxex-dp-sheet", width);
            await Page.Locator("#nxex-dp-sheet [data-dp-cancel]").ClickAsync();

            await form.Locator("[data-open-timepicker]").First.ClickAsync();
            await Page.Locator("#nxex-tp-sheet.open").WaitForAsync();
            await AssertFitsAsync("#nxex-tp-sheet", width);
            await Page.Locator("#nxex-tp-sheet [data-tp-cancel]").ClickAsync();

            var submit = form.Locator(".nxex-leave-submit");
            await submit.ScrollIntoViewIfNeededAsync();
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) .nxex-leave-submit", width);

            var overflow = await Page.Locator("#nxex-leave-modal .nxex-modal-body")
                .EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            Assert.That(overflow, Is.False, $"Horizontal overflow at {width}px");
        }
    }

    [Test]
    public async Task FreshLogin_IgnoresStaleHiddenTimestamp_FromPreviousSession()
    {
        await EnsureDisposableEmployeeLoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.EvaluateAsync("localStorage.setItem('zyHiddenAt', String(Date.now() - 3600000));");
        await Page.FillAsync("input[name='Username']", Username);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(".*/EmployeePortal.*"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.WaitForTimeoutAsync(1000);

        Assert.That(Page.Url, Does.Contain("/EmployeePortal"));
        Assert.That(await Page.EvaluateAsync<string?>("localStorage.getItem('zyHiddenAt')"), Is.Null);
    }

    private async Task LoginAsync()
    {
        await EnsureDisposableEmployeeLoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", Username);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    private async Task EnsureDisposableEmployeeLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(DatabaseName) ||
            !Regex.IsMatch(
                DatabaseName,
                "^SmartAttendance_E2E_[A-Za-z0-9_]+$") ||
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "ZYNORA_E2E_EMPLOYEE_PASSWORD")))
        {
            return;
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;

        var saltBytes = RandomNumberGenerator.GetBytes(32);
        var salt = Convert.ToBase64String(saltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Password,
            saltBytes,
            210_000,
            HashAlgorithmName.SHA256,
            32);
        var hash =
            $"PBKDF2-SHA256$210000${Convert.ToBase64String(key)}";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @EmployeeId int =
            (
                SELECT TOP 1 Id
                FROM dbo.Employees
                WHERE EmployeeNo = 'E2E-001'
                  AND IsDeleted = 0
                ORDER BY Id
            );

            IF @EmployeeId IS NULL
                THROW 51001, 'E2E employee fixture E2E-001 is missing.', 1;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.AppLoginUsers
                WHERE Username = @Username
            )
            BEGIN
                UPDATE dbo.AppLoginUsers
                SET EmployeeId = @EmployeeId,
                    PasswordHash = @PasswordHash,
                    PasswordSalt = @PasswordSalt,
                    Role = 'Employee',
                    IsActive = 1,
                    FailedLoginAttempts = 0,
                    LockoutEndUtc = NULL,
                    LastFailedLoginAt = NULL,
                    MustChangePassword = 0,
                    PasswordChangedAt = SYSUTCDATETIME(),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Username = @Username;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.AppLoginUsers
                (
                    EmployeeId, Username, PasswordHash, PasswordSalt,
                    Role, IsActive, FailedLoginAttempts, LockoutEndUtc,
                    LastFailedLoginAt, MustChangePassword,
                    PasswordChangedAt, CreatedAt
                )
                VALUES
                (
                    @EmployeeId, @Username, @PasswordHash, @PasswordSalt,
                    'Employee', 1, 0, NULL,
                    NULL, 0,
                    SYSUTCDATETIME(), SYSUTCDATETIME()
                );
            END;
            """;
        command.Parameters.AddWithValue("@Username", Username);
        command.Parameters.AddWithValue("@PasswordHash", hash);
        command.Parameters.AddWithValue("@PasswordSalt", salt);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertFitsAsync(string selector, int viewportWidth)
    {
        var box = await Page.Locator(selector).BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, selector);
        Assert.That(box!.X, Is.GreaterThanOrEqualTo(-1), selector);
        Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(viewportWidth + 1), selector);
    }
}
