using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Web.Infrastructure.Imports;

namespace SmartAttendance.Tests;

public sealed class AttendanceImportSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "zynora-attendance-import-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Restricted_scope_allows_only_the_selected_authorized_company()
    {
        Assert.True(AttendanceImportScope.Restricted(new[] { 11 }, 11).AllowsSelectedCompany());
        Assert.False(AttendanceImportScope.Restricted(new[] { 11 }, 22).AllowsSelectedCompany());
        Assert.False(AttendanceImportScope.Restricted(Array.Empty<int>(), 11).AllowsSelectedCompany());
        Assert.True(AttendanceImportScope.Unrestricted(22).AllowsSelectedCompany());
    }

    [Fact]
    public async Task Staging_token_is_actor_and_company_bound_and_single_use()
    {
        Directory.CreateDirectory(_root);
        var keyDirectory = Directory.CreateDirectory(Path.Combine(_root, "keys"));
        var provider = DataProtectionProvider.Create(keyDirectory);
        var environment = new TestEnvironment(_root);
        var store = new AttendanceImportStagingStore(provider, environment);
        await using var content = new MemoryStream(
            "EmployeeCardNumber,AttendanceDate\n10001,2026-08-09 08:00:00\n"u8.ToArray());
        var file = new FormFile(content, 0, content.Length, "file", "attendance.csv");

        var staged = await store.SaveAsync(file, "system-user:7", 11, CancellationToken.None);

        Assert.True(File.Exists(staged.FilePath));
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Claim(staged.Token, "system-user:8", 11));
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Claim(staged.Token, "system-user:7", 22));

        var claimed = store.Claim(staged.Token, "system-user:7", 11);
        Assert.Contains(".processing.csv", claimed.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(claimed.FilePath));
        Assert.Throws<InvalidOperationException>(() =>
            store.Claim(staged.Token, "system-user:7", 11));

        store.Delete(claimed);
        Assert.False(File.Exists(claimed.FilePath));
    }

    public void Dispose()
    {
        var fullRoot = Path.GetFullPath(_root);
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "zynora-attendance-import-tests")) + Path.DirectorySeparatorChar;
        if (fullRoot.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmartAttendance.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
