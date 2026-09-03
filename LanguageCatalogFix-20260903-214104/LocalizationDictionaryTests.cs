using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Reports;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LocalizationDictionaryTests
{
    [Fact]
    public async Task DictionaryService_ImportsActivatesAndDeletesCustomLanguage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zynora-dictionary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalizationDictionary:Path"] = Path.Combine(directory, "dictionary.json")
            }).Build();
            var service = new LocalizationDictionaryService(new TestEnvironment(directory), configuration);
            var sourceKey = (await service.GetRowsAsync()).First().Key;
            await service.SaveTranslationsAsync(
                "en-US",
                new Dictionary<string, string> { [sourceKey] = "Machine draft" },
                machineGenerated: true);
            var machineDraft = (await service.GetRowsAsync()).Single(item =>
                item.CultureCode == "en-US" && item.Key == sourceKey);
            Assert.True(machineDraft.RequiresReview);

            await service.SaveTranslationAsync("en-US", sourceKey, "Reviewed translation");
            var reviewed = (await service.GetRowsAsync()).Single(item =>
                item.CultureCode == "en-US" && item.Key == sourceKey);
            Assert.False(reviewed.RequiresReview);

            var columns = new[]
            {
                new ReportExportService.Column("CultureCode", "رمز اللغة"),
                new ReportExportService.Column("NativeName", "اسم اللغة"),
                new ReportExportService.Column("EnglishName", "الاسم بالإنجليزية"),
                new ReportExportService.Column("Direction", "الاتجاه"),
                new ReportExportService.Column("Key", "النص العربي / المفتاح"),
                new ReportExportService.Column("Translation", "الترجمة")
            };
            var export = ReportExportService.Build("xlsx", "Dictionary", columns,
            [
                new Dictionary<string, string>
                {
                    ["CultureCode"] = "fr-FR",
                    ["NativeName"] = "Français",
                    ["EnglishName"] = "French",
                    ["Direction"] = "ltr",
                    ["Key"] = sourceKey,
                    ["Translation"] = "Traduction"
                }
            ]);
            await using var stream = new MemoryStream(export.Bytes);

            var imported = await service.ImportAsync(stream, "fr-FR.xlsx", replace: true);

            Assert.True(imported.IsNewLanguage);
            Assert.Equal("fr-FR", imported.CultureCode);
            Assert.Equal("Traduction", (await service.GetCatalogAsync("fr-FR"))[sourceKey]);
            Assert.Contains(await service.GetLanguagesAsync(), item => item.Code == "fr-FR" && item.Direction == "ltr");

            await service.DeleteLanguageAsync("fr-FR");
            Assert.Null(await service.FindLanguageAsync("fr-FR"));

            var legacyExport = ReportExportService.Build("xlsx", "Dictionary",
            [
                new ReportExportService.Column("CultureCode", "CultureCode"),
                new ReportExportService.Column("NativeName", "NativeName"),
                new ReportExportService.Column("EnglishName", "EnglishName"),
                new ReportExportService.Column("Direction", "Direction"),
                new ReportExportService.Column("Key", "Key"),
                new ReportExportService.Column("Translation", "Translation")
            ],
            [
                new Dictionary<string, string>
                {
                    ["CultureCode"] = "fr-FR",
                    ["NativeName"] = "Français",
                    ["EnglishName"] = "French",
                    ["Direction"] = "ltr",
                    ["Key"] = sourceKey,
                    ["Translation"] = "Traduction héritée"
                }
            ]);
            await using var legacyStream = new MemoryStream(legacyExport.Bytes);
            var legacyImported = await service.ImportAsync(legacyStream, "legacy-fr-FR.xlsx", replace: true);
            Assert.True(legacyImported.IsNewLanguage);
            Assert.Equal("Traduction héritée", (await service.GetCatalogAsync("fr-FR"))[sourceKey]);

            await service.DeleteLanguageAsync("fr-FR");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteLanguageAsync("ar-IQ"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DictionaryPage_OffersEditingExcelLifecycleAndAdminProtection()
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Settings", "Dictionary.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Settings", "Dictionary.cshtml.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml"));
        var layout = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Shared", "_Layout.cshtml"));
        var program = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Program.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", model, StringComparison.Ordinal);
        Assert.Contains("OnGetExportAsync", model, StringComparison.Ordinal);
        Assert.Contains("OnGetNewLanguageTemplateAsync", model, StringComparison.Ordinal);
        Assert.Contains("OnPostImportAsync", model, StringComparison.Ordinal);
        Assert.Contains("OnPostDeleteLanguageAsync", model, StringComparison.Ordinal);
        Assert.Contains("OnPostAutoTranslateAsync", model, StringComparison.Ordinal);
        Assert.Contains("CultureCode", model, StringComparison.Ordinal);
        Assert.Contains("Translation", model, StringComparison.Ordinal);
        Assert.Contains("رمز اللغة", model, StringComparison.Ordinal);
        Assert.Contains("النص العربي / المفتاح", model, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Save\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"AutoTranslate\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Settings/Dictionary\"", settings, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Settings/Dictionary\"", layout, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ILocalizationDictionaryService, LocalizationDictionaryService>", program, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient<IAutomaticTextTranslator, AzureAutomaticTextTranslator>", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<DynamicDictionaryCultureMiddleware>", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DictionaryService_DiscoversVisibleTextWrittenDirectlyInRazorPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zynora-dictionary-scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var webRoot = Path.Combine(RepoRoot(), "SmartAttendance.Web");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalizationDictionary:Path"] = Path.Combine(directory, "dictionary.json"),
                ["LocalizationDictionary:IncludeScannedSourceKeys"] = "true"
            }).Build();
            var service = new LocalizationDictionaryService(new TestEnvironment(webRoot), configuration);

            var rows = await service.GetRowsAsync();

            Assert.Contains(rows, item => item.CultureCode == "ar-IQ" &&
                item.Key == "بوابة موحدة لتهيئة الشركة والأمن والموارد البشرية والحضور والرواتب والتكاملات.");
            Assert.True(rows.Where(item => item.CultureCode == "ar-IQ").Select(item => item.Key).Distinct().Count() > 630);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DictionaryService_UsesPublishedSourceCatalogWithoutSourceFiles()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "zynora-published-localization-catalog-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            const string publishedKey = "نص واجهة من كتالوج البناء المنشور";
            var catalogPath = Path.Combine(directory, "localization-source-keys.json");
            await File.WriteAllTextAsync(
                catalogPath,
                System.Text.Json.JsonSerializer.Serialize(new[] { publishedKey }));

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LocalizationDictionary:Path"] = Path.Combine(directory, "dictionary.json"),
                    ["LocalizationDictionary:SourceCatalogPath"] = catalogPath,
                    ["LocalizationDictionary:IncludeScannedSourceKeys"] = "true"
                }).Build();

            var service = new LocalizationDictionaryService(
                new TestEnvironment(directory),
                configuration);

            var rows = await service.GetRowsAsync();

            Assert.Contains(rows, item =>
                item.CultureCode == "ar-IQ" &&
                item.Key == publishedKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmartAttendance.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
