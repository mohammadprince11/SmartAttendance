using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public sealed class PeopleAiSmartOnboardingTests : PageTest
{
    private static string BaseUrl =>
        Require("ZYNORA_E2E_BASE_URL").TrimEnd('/');

    private static string Username =>
        Require("ZYNORA_E2E_USERNAME");

    private static string Password =>
        Require("ZYNORA_E2E_PASSWORD");

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? throw new InvalidOperationException(
            "Disposable E2E SQL connection is required.");

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task NationalId_Onboarding_PersistsEmployeeIdentityAndAudit()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Employees/SmartOnboarding");

        await Page.Locator("select[name='CompanyId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "ZYNORA E2E A" });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.GetByRole(AriaRole.Button, new() { Name = "بدء Smart Onboarding" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var sessionId = QueryLong(Page.Url, "SessionId");
        var companyId = QueryInt(Page.Url, "CompanyId");
        Assert.That(sessionId, Is.GreaterThan(0));
        Assert.That(companyId, Is.GreaterThan(0));

        var companyB = await ScalarIntAsync(
            "SELECT Id FROM dbo.Companies WHERE Code = 'E2E-B';");
        await Page.GotoAsync(
            $"{BaseUrl}/Employees/SmartOnboarding?CompanyId={companyB}&SessionId={sessionId}");
        await Expect(Page.Locator(".so-alert--danger"))
            .ToContainTextAsync("لا تملك صلاحية الوصول");
        await Expect(Page.Locator(".so-doc")).ToHaveCountAsync(0);

        await Page.GotoAsync(
            $"{BaseUrl}/Employees/SmartOnboarding?CompanyId={companyId}&SessionId={sessionId}");

        await UploadAsync("NationalId", "e2e-national-id.png", TinyPng);
        await UploadAsync("Contract", "e2e-contract.png", TinyPng);
        await WaitForOnboardingProcessingAsync(sessionId);

        var reviewLink = Page.GetByRole(
            AriaRole.Link,
            new() { Name = "فتح المراجعة البشرية" });
        await Expect(reviewLink).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await reviewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var nationalCard = Page.Locator(
            "article.sor-doc-review",
            new() { HasText = "e2e-national-id.png" });
        await Expect(nationalCard).ToBeVisibleAsync();

        await nationalCard.Locator(
                "form.sor-original-actions button[value='Verified']")
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await AcceptReviewFieldsAsync();
        await ResolveOpenIssuesAsync();
        var markReady = Page.GetByRole(
            AriaRole.Button,
            new() { Name = "اعتماد المراجعة" });
        await Expect(markReady).ToBeEnabledAsync(
            new LocatorAssertionsToBeEnabledOptions { Timeout = 15_000 });
        await markReady.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var employeeNo = Page.Locator("input[name='Finalize.EmployeeNo']:visible");
        if (await employeeNo.CountAsync() > 0)
        {
            await employeeNo.FillAsync($"E2E-AI-{sessionId}");
        }

        var fullName = Page.Locator("input[name='Finalize.FullName']");
        if (string.IsNullOrWhiteSpace(await fullName.InputValueAsync()))
        {
            await fullName.FillAsync("Synthetic People AI Employee");
        }

        await Page.Locator("select[name='Finalize.BranchId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "E2E Branch A" });
        await Page.Locator("select[name='Finalize.DepartmentId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "E2E Department A" });
        await Page.Locator(
                "input[type='checkbox'][name='Finalize.IsCitizen']")
            .CheckAsync();

        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "إنشاء الموظف وربط المستندات" })
            .ClickAsync();
        await Page.WaitForURLAsync(
            new Regex(@".*/Employees/Profile.*"),
            new() { Timeout = 20_000, WaitUntil = WaitUntilState.DOMContentLoaded });

        var employeeId = QueryInt(Page.Url, "id");
        Assert.That(employeeId, Is.GreaterThan(0));

        Assert.That(await ScalarIntAsync(
            "SELECT CompanyId FROM dbo.Employees WHERE Id = @EmployeeId;",
            ("@EmployeeId", employeeId)), Is.EqualTo(companyId));

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.EmployeeIdentityDocuments
            WHERE EmployeeId = @EmployeeId
              AND CompanyId = @CompanyId
              AND DocumentType = 'NationalId'
              AND NationalNumber = '199276728473'
              AND FamilyNumber = '1010E1876147874699';
            """,
            ("@EmployeeId", employeeId),
            ("@CompanyId", companyId)), Is.EqualTo(1));

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*) FROM dbo.PeopleAiAuditLogs
            WHERE EmployeeId = @EmployeeId AND SessionId = @SessionId
              AND Operation = 'EmployeeCreated' AND Success = 1;
            """,
            ("@EmployeeId", employeeId),
            ("@SessionId", sessionId)), Is.EqualTo(1));

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.EmployeeOnboardingSessions
            WHERE Id = @SessionId
              AND Status = 'Completed'
              AND CreatedEmployeeId = @EmployeeId;
            """,
            ("@SessionId", sessionId),
            ("@EmployeeId", employeeId)), Is.EqualTo(1));

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.ProtectedFileAssets
            WHERE OwnerType = 'OnboardingSession'
              AND OwnerId = @SessionId
              AND DeletedAt IS NULL;
            """,
            ("@SessionId", sessionId)), Is.EqualTo(0));
    }

    [Test]
    public async Task Passport_Onboarding_PersistsPassportIdentity()
    {
        await LoginAsync();
        var (sessionId, companyId) = await StartSessionAsync();

        await UploadAsync("Passport", "e2e-passport.png", TinyPng);
        await UploadAsync("Residence", "e2e-residence.png", TinyPng);
        await UploadAsync("Contract", "e2e-contract-expat.png", TinyPng);
        await WaitForOnboardingProcessingAsync(sessionId);

        await Page.GetByRole(
                AriaRole.Link,
                new() { Name = "فتح المراجعة البشرية" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await ReviewDocumentOriginalAndExpiryAsync(
            "e2e-passport.png",
            "2035-01-02");
        await ReviewDocumentOriginalAndExpiryAsync(
            "e2e-residence.png",
            "2035-01-02");

        await AcceptReviewFieldsAsync();
        await ResolveOpenIssuesAsync();

        var markReady = Page.GetByRole(
            AriaRole.Button,
            new() { Name = "اعتماد المراجعة" });
        await Expect(markReady).ToBeEnabledAsync();
        await markReady.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await FillRequiredFinalizeFieldsAsync(sessionId, isCitizen: false);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "إنشاء الموظف وربط المستندات" })
            .ClickAsync();
        await Page.WaitForURLAsync(
            new Regex(@".*/Employees/Profile.*"),
            new() { Timeout = 20_000, WaitUntil = WaitUntilState.DOMContentLoaded });

        var employeeId = QueryInt(Page.Url, "id");
        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*) FROM dbo.EmployeeIdentityDocuments
            WHERE EmployeeId = @EmployeeId AND CompanyId = @CompanyId
              AND DocumentType = 'Passport'
              AND DocumentNumber = 'L898902C3';
            """,
            ("@EmployeeId", employeeId),
            ("@CompanyId", companyId)), Is.EqualTo(1));

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*) FROM dbo.EmployeeIdentityDocuments
            WHERE EmployeeId = @EmployeeId AND CompanyId = @CompanyId
              AND DocumentType = 'Residence';
            """,
            ("@EmployeeId", employeeId),
            ("@CompanyId", companyId)), Is.EqualTo(1));
    }

    [Test]
    public async Task Cv_Onboarding_ExtractsAndReviewsContactData()
    {
        await LoginAsync();
        var (sessionId, _) = await StartSessionAsync();

        await UploadAsync("CV", "e2e-cv.png", TinyPng);
        await WaitForOnboardingProcessingAsync(sessionId);

        await Page.GetByRole(
                AriaRole.Link,
                new() { Name = "فتح المراجعة البشرية" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var phone = Page.Locator(
            "article.sor-field",
            new() { HasText = "الهاتف" })
            .Locator("input[name='reviewedValue']");
        var email = Page.Locator(
            "article.sor-field",
            new() { HasText = "البريد الشخصي" })
            .Locator("input[name='reviewedValue']");

        await Expect(phone).ToHaveValueAsync("+9647701234567");
        await Expect(email).ToHaveValueAsync("candidate.e2e@example.test");

        await AcceptReviewFieldsAsync();

        Assert.That(await ScalarIntAsync(
            """
            SELECT COUNT(*) FROM dbo.DocumentExtractedFields f
            JOIN dbo.DocumentExtractionRuns r ON r.Id = f.ExtractionRunId
            JOIN dbo.OnboardingDocuments d ON d.Id = r.OnboardingDocumentId
            WHERE d.SessionId = @SessionId
              AND f.FieldKey IN ('Phone', 'PersonalEmail')
              AND f.ReviewStatus IN ('Accepted', 'Modified');
            """,
            ("@SessionId", sessionId)), Is.EqualTo(2));
    }

    [Test]
    public async Task HrOfficer_PeopleAiPermissions_AreEnforcedIndependently()
    {
        const string userName = "e2e-peopleai-hr";
        await SeedHrLoginAsync(userName);
        await LoginAsAsync(userName);

        await Page.GotoAsync($"{BaseUrl}/Employees/SmartOnboarding");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(Page).ToHaveURLAsync(new Regex(".*/AccessDenied$"));

        await GrantPermissionAsync(
            userName,
            "People.AI.ProcessDocuments");

        await Page.GotoAsync($"{BaseUrl}/Employees/SmartOnboarding");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(Page).ToHaveURLAsync(
            new Regex(".*/Employees/SmartOnboarding.*"));

        await Page.Locator("select[name='CompanyId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "ZYNORA E2E A" });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "بدء Smart Onboarding" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var sessionId = QueryLong(Page.Url, "SessionId");
        await UploadAsync("NationalId", "e2e-permission-id.png", TinyPng);
        await UploadAsync("Contract", "e2e-permission-contract.png", TinyPng);
        await WaitForOnboardingProcessingAsync(sessionId);

        await Page.GetByRole(
                AriaRole.Link,
                new() { Name = "فتح المراجعة البشرية" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var nationalCard = Page.Locator(
            "article.sor-doc-review",
            new() { HasText = "e2e-permission-id.png" });
        await Expect(nationalCard.Locator(
            "form.sor-original-actions")).ToHaveCountAsync(0);

        await GrantPermissionAsync(
            userName,
            "People.VerifyOriginalDocument");
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        nationalCard = Page.Locator(
            "article.sor-doc-review",
            new() { HasText = "e2e-permission-id.png" });
        await Expect(nationalCard.Locator(
            "form.sor-original-actions")).ToHaveCountAsync(1);
        await nationalCard.Locator(
                "form.sor-original-actions button[value='Verified']")
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await AcceptReviewFieldsAsync();
        await ResolveOpenIssuesAsync();

        var ready = Page.GetByRole(
            AriaRole.Button,
            new() { Name = "اعتماد المراجعة" });
        await Expect(ready).ToBeEnabledAsync();
        await ready.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Expect(Page.Locator(".sor-alert--danger"))
            .ToContainTextAsync("People.Create");
        await Expect(Page.GetByRole(
            AriaRole.Button,
            new() { Name = "إنشاء الموظف وربط المستندات" }))
            .ToBeDisabledAsync();
    }

    [Test]
    public async Task InvalidPngSignature_IsRejectedBeforeStorage()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Employees/SmartOnboarding");
        await Page.Locator("select[name='CompanyId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "ZYNORA E2E A" });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "بدء Smart Onboarding" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var sessionId = QueryLong(Page.Url, "SessionId");
        var companyId = QueryInt(Page.Url, "CompanyId");

        await Page.Locator("select[name='DeclaredDocumentType']")
            .SelectOptionAsync("NationalId");
        await Page.Locator("input[data-upload-file]").SetInputFilesAsync(
            new FilePayload
            {
                Name = "invalid.png",
                MimeType = "image/png",
                Buffer = "not-a-png"u8.ToArray()
            });
        await Page.Locator("button[data-upload-submit]").ClickAsync();

        await Expect(Page.Locator("[data-upload-message]"))
            .ToContainTextAsync(
                "محتوى الملف الحقيقي لا يطابق امتداده",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        Assert.That(await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.ProtectedFileAssets WHERE OwnerType='OnboardingSession' AND OwnerId=@SessionId;",
            ("@SessionId", sessionId)), Is.EqualTo(0));
        Assert.That(companyId, Is.GreaterThan(0));
    }

    private Task LoginAsync() => LoginAsAsync(Username);

    private async Task LoginAsAsync(string userName)
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", userName);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(
            new Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15_000, WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    private static Task SeedHrLoginAsync(string userName) =>
        ExecuteAsync(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.AppLoginUsers
                WHERE Username = @Username
            )
            INSERT INTO dbo.AppLoginUsers
                (EmployeeId, Username, PasswordHash, PasswordSalt,
                 Role, IsActive, PasswordChangedAt, CreatedAt)
            SELECT NULL, @Username, PasswordHash, PasswordSalt,
                   'HR Officer', 1, SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM dbo.AppLoginUsers
            WHERE Username = 'admin';
            """,
            ("@Username", userName));

    private static Task GrantPermissionAsync(
        string userName,
        string permissionCode) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.SystemUserPermissions
                (SystemUserId, PermissionId, Effect, ScopeType,
                 CreatedAt, IsDeleted)
            SELECT su.Id, p.Id, 1, 1, SYSUTCDATETIME(), 0
            FROM dbo.SystemUsers su
            JOIN dbo.Permissions p ON p.Code = @PermissionCode
            WHERE su.UserName = @Username
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.SystemUserPermissions existing
                  WHERE existing.SystemUserId = su.Id
                    AND existing.PermissionId = p.Id
                    AND existing.IsDeleted = 0
              );
            """,
            ("@Username", userName),
            ("@PermissionCode", permissionCode));
    private async Task<(long SessionId, int CompanyId)> StartSessionAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/Employees/SmartOnboarding");
        await Page.Locator("select[name='CompanyId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "ZYNORA E2E A" });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "بدء Smart Onboarding" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        return (
            QueryLong(Page.Url, "SessionId"),
            QueryInt(Page.Url, "CompanyId"));
    }

    private async Task FillRequiredFinalizeFieldsAsync(
        long sessionId,
        bool isCitizen)
    {
        var employeeNo = Page.Locator("input[name='Finalize.EmployeeNo']:visible");
        if (await employeeNo.CountAsync() > 0)
        {
            await employeeNo.FillAsync($"E2E-AI-{sessionId}");
        }

        var fullName = Page.Locator("input[name='Finalize.FullName']");
        if (string.IsNullOrWhiteSpace(await fullName.InputValueAsync()))
        {
            await fullName.FillAsync("Synthetic People AI Employee");
        }

        await Page.Locator("select[name='Finalize.BranchId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "E2E Branch A" });
        await Page.Locator("select[name='Finalize.DepartmentId']")
            .SelectOptionAsync(new SelectOptionValue { Label = "E2E Department A" });

        var citizen = Page.Locator(
            "input[type='checkbox'][name='Finalize.IsCitizen']");
        if (isCitizen)
        {
            await citizen.CheckAsync();
        }
        else
        {
            await citizen.UncheckAsync();
        }
    }

    private async Task UploadAsync(
        string documentType,
        string fileName,
        byte[] bytes)
    {
        await Page.Locator("select[name='DeclaredDocumentType']")
            .SelectOptionAsync(documentType);
        await Page.Locator("input[data-upload-file]")
            .SetInputFilesAsync(new FilePayload
            {
                Name = fileName,
                MimeType = "image/png",
                Buffer = bytes
            });

        await Page.Locator("button[data-upload-submit]").ClickAsync();
        await Expect(Page.Locator(
                $"article.so-doc:has-text('{fileName}')"))
            .ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    private async Task ReviewDocumentOriginalAndExpiryAsync(
        string fileName,
        string expiryDate)
    {
        var card = Page.Locator(
            "article.sor-doc-review",
            new() { HasText = fileName });

        await card.Locator("input[name='expiryDate']")
            .FillAsync(expiryDate);
        await card.GetByRole(
                AriaRole.Button,
                new() { Name = "حفظ التاريخ" })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        card = Page.Locator(
            "article.sor-doc-review",
            new() { HasText = fileName });
        await card.Locator(
                "form.sor-original-actions button[value='Verified']")
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private async Task AcceptReviewFieldsAsync()
    {
        var acceptAll = Page.GetByRole(
            AriaRole.Button,
            new() { Name = "اعتماد الكل" });
        if (await acceptAll.CountAsync() > 0 &&
            await acceptAll.IsVisibleAsync())
        {
            await acceptAll.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }

        for (var iteration = 0; iteration < 12; iteration++)
        {
            var required = Page.Locator(
                "article.sor-field:has(.is-required)");
            if (await required.CountAsync() == 0)
            {
                return;
            }

            var card = required.First;
            var input = card.Locator("input[name='reviewedValue']");
            var value = await input.InputValueAsync();

            if (string.IsNullOrWhiteSpace(value))
            {
                var label = await card.Locator(".sor-field-meta strong")
                    .InnerTextAsync();
                var replacement = label.Contains("تاريخ الميلاد")
                    ? "1990-01-02"
                    : label.Contains("تاريخ الانتهاء")
                        ? "2035-01-02"
                        : "Synthetic E2E";
                await input.FillAsync(replacement);
                await card.Locator("button[value='Modify']").ClickAsync();
            }
            else
            {
                await card.Locator("button[value='Accept']").ClickAsync();
            }

            await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }

        Assert.Fail("Required People AI review fields did not converge.");
    }

    private async Task ResolveOpenIssuesAsync()
    {
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var form = Page.Locator("form.sor-resolution");
            if (await form.CountAsync() == 0)
            {
                return;
            }

            var first = form.First;
            await first.Locator("input[name='resolution']")
                .FillAsync("Synthetic E2E reviewer decision");
            await first.GetByRole(
                    AriaRole.Button,
                    new() { Name = "توثيق وإغلاق" })
                .ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }

        Assert.Fail("Open People AI review issues did not converge.");
    }

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Ignore($"{name} is required for isolated E2E.");
        }

        return value!;
    }

    private static long QueryLong(string url, string name)
    {
        var match = Regex.Match(
            url,
            $@"(?:\?|&){Regex.Escape(name)}=(\d+)",
            RegexOptions.IgnoreCase);
        return match.Success ? long.Parse(match.Groups[1].Value) : 0;
    }

    private static int QueryInt(string url, string name) =>
        checked((int)QueryLong(url, name));
    private static async Task WaitForOnboardingProcessingAsync(long sessionId)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var unfinished = await ScalarIntAsync(
                """
                SELECT COUNT(*)
                FROM dbo.OnboardingDocuments
                WHERE SessionId = @SessionId
                  AND ProcessingStatus IN ('Queued', 'Processing', 'Failed');
                """,
                ("@SessionId", sessionId));

            if (unfinished == 0)
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Onboarding document processing did not finish.");
    }

    private static async Task ExecuteAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value);
        }

        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwC" +
            "AAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
