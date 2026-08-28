using System.Xml.Linq;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LocalizationContractTests
{
    [Fact]
    public void Application_ConfiguresServerSideLocalizationAndSafeCulturePersistence()
    {
        var program = ReadWeb("Program.cs");
        var handler = ReadWeb("Pages", "Culture", "Set.cshtml.cs");

        Assert.Contains("AddLocalization", program, StringComparison.Ordinal);
        Assert.Contains("UseRequestLocalization", program, StringComparison.Ordinal);
        Assert.Contains("CookieRequestCultureProvider", program, StringComparison.Ordinal);
        Assert.Contains("ZynoraSupportedCultures.All", program, StringComparison.Ordinal);
        Assert.Contains("ZynoraSupportedCultures.TryGet", handler, StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymous]", handler, StringComparison.Ordinal);
        Assert.Contains("LocalRedirect", handler, StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Redirect(returnUrl", handler, StringComparison.Ordinal);

        var publicPaths = ReadWeb("Infrastructure", "Security", "PublicPathPolicy.cs");
        Assert.Contains("/culture/catalog", publicPaths, StringComparison.Ordinal);
        Assert.Contains("/culture/set", publicPaths, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_UsesCultureDirectionAndPostsLanguageChanges()
    {
        var layout = ReadWeb("Pages", "Shared", "_Layout.cshtml");

        Assert.Contains("lang=\"@currentCulture.Name\"", layout, StringComparison.Ordinal);
        Assert.Contains("dir=\"@currentDirection\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Culture/Set\"", layout, StringComparison.Ordinal);
        Assert.Contains("zynora-direction.css", layout, StringComparison.Ordinal);
        Assert.Contains("zynora-runtime-localization.js", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<html lang=\"ar\" dir=\"rtl\"", layout, StringComparison.Ordinal);

        var login = ReadWeb("Pages", "Account", "Login.cshtml");
        Assert.Contains("login-language-switcher", login, StringComparison.Ordinal);
        Assert.Contains("ZynoraSupportedCultures.All", login, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Culture/Set\"", login, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedCultures_IncludeArabicEnglishAndKurdish()
    {
        var registry = ReadWeb("Localization", "ZynoraSupportedCultures.cs");

        Assert.Contains("ar-IQ", registry, StringComparison.Ordinal);
        Assert.Contains("en-US", registry, StringComparison.Ordinal);
        Assert.Contains("ckb-IQ", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndKurdishCatalogs_HaveMatchingNonEmptyKeys()
    {
        var english = ReadCatalog("SharedResource.en-US.resx");
        var kurdish = ReadCatalog("SharedResource.ckb-IQ.resx");

        Assert.True(english.Count >= 250, $"Expected a broad shared catalog, found {english.Count} entries.");
        Assert.Equal(english.Keys.Order(), kurdish.Keys.Order());
        Assert.DoesNotContain(english, item => string.IsNullOrWhiteSpace(item.Value));
        Assert.DoesNotContain(kurdish, item => string.IsNullOrWhiteSpace(item.Value));
    }

    [Fact]
    public void LegacyLocalizationBridge_UsesExactTextAndSupportsDynamicContent()
    {
        var script = ReadWeb("wwwroot", "js", "zynora-runtime-localization.js");

        Assert.Contains("/Culture/Catalog?culture=", script, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("Object.prototype.hasOwnProperty.call", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadCatalog(string fileName) =>
        XDocument.Load(Path.Combine(RepoRoot(), "SmartAttendance.Web", "Resources", fileName))
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "SmartAttendance.Web" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
