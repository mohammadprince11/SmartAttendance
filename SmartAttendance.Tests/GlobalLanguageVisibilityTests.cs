using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using SmartAttendance.Web.Infrastructure.Localization;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class GlobalLanguageVisibilityTests
{
    [Fact]
    public async Task HiddenLanguage_IsRemovedFromVisibleCatalog_ButPreservedForAdministration()
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "zynora-language-visibility-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["LocalizationDictionary:Path"] =
                                Path.Combine(
                                    directory,
                                    "dictionary.json")
                        })
                    .Build();

            var service =
                new LocalizationDictionaryService(
                    new TestEnvironment(directory),
                    configuration);

            await service.AddLanguageAsync(
                "es-ES",
                "Español",
                "Spanish",
                "ltr");

            Assert.Contains(
                await service.GetLanguagesAsync(),
                item =>
                    item.Code == "es-ES");

            await service.SetLanguageHiddenAsync(
                "es-ES",
                true);

            Assert.DoesNotContain(
                await service.GetLanguagesAsync(),
                item =>
                    item.Code == "es-ES");

            Assert.Contains(
                await service.GetAllLanguagesAsync(),
                item =>
                    item.Code == "es-ES" &&
                    item.IsHidden);

            await service.SetLanguageHiddenAsync(
                "es-ES",
                false);

            Assert.Contains(
                await service.GetLanguagesAsync(),
                item =>
                    item.Code == "es-ES");
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void UiCulturePipeline_UsesVisibleLanguageCatalog()
    {
        var root = RepoRoot();

        var middleware =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "SmartAttendance.Web",
                    "Infrastructure",
                    "Localization",
                    "DynamicDictionaryCultureMiddleware.cs"));

        var setPage =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "SmartAttendance.Web",
                    "Pages",
                    "Culture",
                    "Set.cshtml.cs"));

        var catalogPage =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "SmartAttendance.Web",
                    "Pages",
                    "Culture",
                    "Catalog.cshtml.cs"));

        Assert.Contains(
            "dictionary.GetLanguagesAsync",
            middleware,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Request.Query[\"culture\"]",
            middleware,
            StringComparison.Ordinal);

        Assert.Contains(
            "_dictionary.GetLanguagesAsync",
            setPage,
            StringComparison.Ordinal);

        Assert.Contains(
            "_dictionary.GetLanguagesAsync",
            catalogPage,
            StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (
            directory is not null &&
            !File.Exists(
                Path.Combine(
                    directory.FullName,
                    "SmartAttendance.slnx")))
        {
            directory =
                directory.Parent;
        }

        return Assert
            .IsType<DirectoryInfo>(
                directory)
            .FullName;
    }

    private sealed class TestEnvironment(
        string contentRoot)
        : IWebHostEnvironment
    {
        public string ApplicationName
            { get; set; } =
            "SmartAttendance.Tests";

        public IFileProvider WebRootFileProvider
            { get; set; } =
            new NullFileProvider();

        public string WebRootPath
            { get; set; } =
            contentRoot;

        public string EnvironmentName
            { get; set; } =
            "Development";

        public string ContentRootPath
            { get; set; } =
            contentRoot;

        public IFileProvider ContentRootFileProvider
            { get; set; } =
            new PhysicalFileProvider(
                contentRoot);
    }
}