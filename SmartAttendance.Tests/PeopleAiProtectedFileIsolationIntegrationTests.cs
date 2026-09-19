using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Controllers;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiProtectedFileIsolationIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private SyntheticTenantSeeder _seeder = null!;
    private bool _dbAvailable;
    private int _companyA;
    private int _employeeB;
    private readonly string _contentRoot =
        Path.Combine(
            Path.GetTempPath(),
            "zynora-protected-isolation-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString)
                .Options);

        try
        {
            await _db.Database.OpenConnectionAsync();
            var connection = _db.Database.GetDbConnection();
            _seeder = new SyntheticTenantSeeder(connection);

            _companyA = await ScalarIntAsync(
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-A';");
            _employeeB = await ScalarIntAsync(
                "SELECT Id FROM dbo.Employees WHERE EmployeeNo='E2E-002';");

            _dbAvailable = _companyA > 0 && _employeeB > 0;
            Directory.CreateDirectory(_contentRoot);
        }
        catch
        {
            _dbAvailable = false;
        }
    }
    public async Task DisposeAsync()
    {
        if (_seeder is not null)
        {
            await _seeder.CleanupAsync();
        }

        if (_db is not null)
        {
            try
            {
                await _db.Database.CloseConnectionAsync();
            }
            catch
            {
            }

            await _db.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    [SkippableFact]
    public async Task CompanyA_User_IsDeniedDirectTokenAndProfileFileIdForCompanyBEmployee()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");

        var protectedFiles = new ProtectedFileService(
            new EphemeralDataProtectionProvider(),
            new TestWebHostEnvironment(_contentRoot),
            new CleanScanner(),
            Options.Create(new MalwareScanningOptions()),
            NullLogger<ProtectedFileService>.Instance);

        var controller = new EmployeeFilesController(
            _db,
            new AllowProfilePermissionService(),
            new FixedScopeService(new PeopleDataScope
            {
                AllowedCompanyIds = [_companyA]
            }),
            new TestWebHostEnvironment(_contentRoot),
            protectedFiles);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext()
        };

        var storageKey = ProtectedFileStore.BuildStorageKey(
            _employeeB,
            "identity",
            ".png");

        var signedUrl = protectedFiles.BuildUrl(
            _employeeB,
            ProtectedFileReference.ToStoredPath(storageKey));

        var token = Uri.UnescapeDataString(
            signedUrl[(signedUrl.IndexOf("?t=", StringComparison.Ordinal) + 3)..]);

        var tokenResult = await controller.Download(token);
        Assert.IsType<ForbidResult>(tokenResult);
        var profileFileId = await _seeder.InsertAsync(
            "EmployeeProfileFiles",
            new Dictionary<string, object>
            {
                ["EmployeeId"] = _employeeB
            });

        var idResult = await controller.DownloadProfileFile(profileFileId);
        Assert.IsType<ForbidResult>(idResult);
    }

    [Fact]
    public async Task TamperedProtectedToken_IsNotFound()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options);

        var protectedFiles = new ProtectedFileService(
            new EphemeralDataProtectionProvider(),
            new TestWebHostEnvironment(_contentRoot),
            new CleanScanner(),
            Options.Create(new MalwareScanningOptions()),
            NullLogger<ProtectedFileService>.Instance);

        var controller = new EmployeeFilesController(
            db,
            new AllowProfilePermissionService(),
            new FixedScopeService(PeopleDataScope.Unrestricted()),
            new TestWebHostEnvironment(_contentRoot),
            protectedFiles)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var result = await controller.Download("tampered-token");
        Assert.IsType<NotFoundResult>(result);
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "e2e-company-a-user"),
                new Claim(ClaimTypes.Role, "HR Officer"),
                new Claim("SystemUserId", "9001")
            ],
            "Test");

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }
    private async Task<int> ScalarIntAsync(string sql)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class AllowProfilePermissionService :
        IPermissionAuthorizationService
    {
        public Task<bool> HasDirectGrantAsync(
            int systemUserId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> HasPermissionAsync(
            int systemUserId,
            string permissionCode,
            bool compatibilityAllowed = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> HasGlobalPermissionAsync(
            int systemUserId,
            string permissionCode,
            bool compatibilityAllowed = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<PeopleDataScope> GetPeopleDataScopeAsync(
            int systemUserId,
            string permissionCode,
            bool compatibilityUnrestricted = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PeopleDataScope.Unrestricted());

        public Task<bool> CanAccessEmployeeAsync(
            int systemUserId,
            string permissionCode,
            int employeeId,
            bool compatibilityAllowed = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
    private sealed class FixedScopeService(
        PeopleDataScope scope) : IEffectiveScopeService
    {
        public Task<PeopleDataScope> GetEmployeesAccessScopeAsync(
            int systemUserId,
            bool isAdmin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);
    }

    private sealed class CleanScanner : IFileThreatScanner
    {
        public Task<FileThreatScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FileThreatScanResult.Clean);
    }

    private sealed class TestWebHostEnvironment(
        string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } =
            "SmartAttendance.Tests";
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } =
            Path.Combine(contentRootPath, "wwwroot");
        public string EnvironmentName { get; set; } =
            "Testing";
        public string ContentRootPath { get; set; } =
            contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
