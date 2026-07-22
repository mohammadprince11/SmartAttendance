using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Integration tests for the theme persistence + publish lifecycle against the
/// real self-healing SQL store. Uses sentinel company ids that never collide
/// with real companies and cleans them up around every test. Requires the local
/// SQL Server; skipped automatically when it is unreachable.
/// </summary>
public sealed class ThemeStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private const int CompanyA = 900001;
    private const int CompanyB = 900002;

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await ThemeStore.EnsureAsync(_db);
            await CleanupAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dbAvailable)
        {
            await CleanupAsync();
        }
        await _db.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        await SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.ExecuteAsync(
            _db,
            "DELETE FROM ThemeVersions WHERE CompanyId IN (@a, @b); DELETE FROM CompanyBrandingProfiles WHERE CompanyId IN (@a, @b);",
            command =>
            {
                SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.AddParameter(command, "@a", CompanyA);
                SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.AddParameter(command, "@b", CompanyB);
            });
    }

    private async Task<ThemeStore.ThemeVersion> PublishBrandAsync(int companyId, string primaryHex)
    {
        await ThemeStore.SaveBrandingProfileAsync(_db, new ThemeStore.BrandingProfile
        {
            CompanyId = companyId,
            PrimaryHex = primaryHex,
        });
        var version = await ThemeStore.CompileDraftAsync(_db, companyId);
        Assert.NotNull(version);
        var ok = await ThemeStore.PublishAsync(_db, companyId, version!.Id);
        Assert.True(ok);
        return version;
    }

    [SkippableFact]
    public async Task NoPublishedTheme_ResolvesToNull()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var published = await ThemeStore.GetActivePublishedAsync(_db, CompanyA);
        Assert.Null(published);
    }

    [SkippableFact]
    public async Task Publish_MakesTheCompiledCssActive()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        await PublishBrandAsync(CompanyA, "#3AA0FF");

        var published = await ThemeStore.GetActivePublishedAsync(_db, CompanyA);
        Assert.NotNull(published);
        Assert.Contains("--brand-primary:#3AA0FF", published!.CompiledCss);
    }

    [SkippableFact]
    public async Task Publish_IsIsolatedPerCompany()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        await PublishBrandAsync(CompanyA, "#3AA0FF");

        // Company B never published, so it must still resolve to nothing.
        Assert.Null(await ThemeStore.GetActivePublishedAsync(_db, CompanyB));
        Assert.NotNull(await ThemeStore.GetActivePublishedAsync(_db, CompanyA));
    }

    [SkippableFact]
    public async Task Republish_ArchivesPrevious_AndActivatesNewest()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var first = await PublishBrandAsync(CompanyA, "#3AA0FF");
        var second = await PublishBrandAsync(CompanyA, "#E23744");

        var active = await ThemeStore.GetActivePublishedAsync(_db, CompanyA);
        Assert.Equal(second.Id, active!.VersionId);
        Assert.Contains("--brand-primary:#E23744", active.CompiledCss);

        var firstRow = await ThemeStore.GetVersionAsync(_db, first.Id);
        Assert.Equal(ThemeStore.StatusArchived, firstRow!.Status);
    }

    [SkippableFact]
    public async Task DeletePublishedVersion_FallsBackToDefault()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var version = await PublishBrandAsync(CompanyA, "#3AA0FF");

        var wasPublished = await ThemeStore.DeleteVersionAsync(_db, CompanyA, version.Id);
        Assert.True(wasPublished);
        Assert.Null(await ThemeStore.GetActivePublishedAsync(_db, CompanyA));
    }

    [SkippableFact]
    public async Task Rollback_RepublishesOlderVersion()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        var first = await PublishBrandAsync(CompanyA, "#3AA0FF");
        await PublishBrandAsync(CompanyA, "#E23744");

        // Rollback = publish the older version again.
        var ok = await ThemeStore.PublishAsync(_db, CompanyA, first.Id);
        Assert.True(ok);

        var active = await ThemeStore.GetActivePublishedAsync(_db, CompanyA);
        Assert.Equal(first.Id, active!.VersionId);
    }

    [SkippableFact]
    public async Task UnreadableBrand_IsRejected_AndNotPublishable()
    {
        Skip.IfNot(_dbAvailable, "Local SQL Server not reachable.");

        await ThemeStore.SaveBrandingProfileAsync(_db, new ThemeStore.BrandingProfile
        {
            CompanyId = CompanyA,
            PrimaryHex = "#0A0A0A", // fails contrast on the dark shell
        });
        var version = await ThemeStore.CompileDraftAsync(_db, CompanyA);

        Assert.NotNull(version);
        Assert.Equal(ThemeStore.StatusRejected, version!.Status);
        Assert.False(await ThemeStore.PublishAsync(_db, CompanyA, version.Id));
        Assert.Null(await ThemeStore.GetActivePublishedAsync(_db, CompanyA));
    }
}
