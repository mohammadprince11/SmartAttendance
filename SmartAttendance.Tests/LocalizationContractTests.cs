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
        Assert.Contains("FindLanguageAsync", handler, StringComparison.Ordinal);
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
        Assert.Contains("LocalizationDictionary.GetLanguagesAsync", login, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Culture/Set\"", login, StringComparison.Ordinal);
        Assert.Contains("id=\"login-language-form\"", login, StringComparison.Ordinal);
        Assert.Contains("languageSelect.addEventListener(\"change\"", login, StringComparison.Ordinal);
        Assert.Contains("languageForm.requestSubmit()", login, StringComparison.Ordinal);
        Assert.DoesNotContain("<button type=\"submit\">@T[\"تطبيق\"]</button>", login, StringComparison.Ordinal);

        var refresh = ReadWeb("wwwroot", "css", "zynora-refresh-2026.css");
        Assert.Contains("id=\"login-theme-toggle\"", login, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"click\"", login, StringComparison.Ordinal);
        Assert.Contains("window.ZynoraTheme.toggle()", login, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(\"ZY.Theme\", nextTheme)", login, StringComparison.Ordinal);
        Assert.Contains(".login-language-switcher label", refresh, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap", refresh, StringComparison.Ordinal);
        Assert.Contains("max-width: 230px", refresh, StringComparison.Ordinal);
        Assert.Contains(".login-language-switcher .login-theme-toggle svg", refresh, StringComparison.Ordinal);
        Assert.Contains("padding: 0 !important", refresh, StringComparison.Ordinal);
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
    public void LoginMissionAndPrinciples_AreLocalizedInEverySupportedLanguage()
    {
        const string mission = "نؤمن أن مستقبل الموارد البشرية لا يُبنى على كثرة الإجراءات، بل على منظومة ذكية توحّد البيانات، وتفهم الإنسان، وتحوّل كل معلومة إلى قرار أكثر دقة، وكل عملية إلى تجربة أكثر كفاءة.";
        const string principles = "الدقة • الأمان • الذكاء • الكفاءة";
        var login = ReadWeb("Pages", "Account", "Login.cshtml");
        var english = ReadCatalog("SharedResource.en-US.resx");
        var kurdish = ReadCatalog("SharedResource.ckb-IQ.resx");

        Assert.Contains($"@T[\"{mission}\"]", login, StringComparison.Ordinal);
        Assert.Contains($"@T[\"{principles}\"]", login, StringComparison.Ordinal);
        Assert.DoesNotContain("@T[\"الموظفون\"] · @T[\"الحضور والانصراف\"] · @T[\"الرواتب\"]", login, StringComparison.Ordinal);

        Assert.Equal("We believe the future of Human Resources is not built on the complexity of processes, but on an intelligent ecosystem that unifies data, understands people, transforms every insight into a more precise decision, and every process into a more efficient experience.", english[mission]);
        Assert.Equal("Accuracy • Security • Intelligence • Efficiency", english[principles]);
        Assert.Equal("ئێمە باوەڕمان وایە کە داهاتووی بەڕێوەبردنی سەرچاوە مرۆییەکان بە زۆری پرۆسە و ڕێکارەکان دروست نابێت، بەڵکو بە سیستەمێکی زیرەک کە داتا یەکدەخات، مرۆڤ تێدەگات، هەر زانیارییەک دەگۆڕێت بۆ بڕیارێکی وردتر، و هەر پرۆسەیەک بۆ ئەزموونێکی کاراتر و بەرهەمدارتر.", kurdish[mission]);
        Assert.Equal("وردبینی • ئاسایش • زیرەکی • کارامەیی", kurdish[principles]);
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
        var catalog = ReadWeb("Pages", "Culture", "Catalog.cshtml.cs");

        Assert.Contains("/Culture/Catalog?culture=", script, StringComparison.Ordinal);
        Assert.Contains("cache: \"no-store\"", script, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("Object.prototype.hasOwnProperty.call", script, StringComparison.Ordinal);
        Assert.Contains("translateComposed", script, StringComparison.Ordinal);
        Assert.Contains("translateTemplate", script, StringComparison.Ordinal);
        Assert.Contains("buildTemplate", script, StringComparison.Ordinal);
        Assert.Contains("document.title = translateValue(document.title)", script, StringComparison.Ordinal);
        Assert.Contains("characterData: true", script, StringComparison.Ordinal);
        Assert.Contains("attributes: true", script, StringComparison.Ordinal);
        Assert.Contains("data-zy-no-localize", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NoStore = true", catalog, StringComparison.Ordinal);
        Assert.Contains("OnGetAsync(string? culture)", catalog, StringComparison.Ordinal);
        Assert.Contains("_dictionary.FindLanguageAsync", catalog, StringComparison.Ordinal);
        Assert.Contains("direction = requested.Direction", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanySetup_IsCoveredInEnglishAndKurdishIncludingDynamicCounts()
    {
        var english = ReadCatalog("SharedResource.en-US.resx");
        var kurdish = ReadCatalog("SharedResource.ckb-IQ.resx");
        var requiredKeys = new[]
        {
            "إعداد الشركة وهيكل العمل",
            "إدارة هوية الشركة، مواقع العمل، الأقسام، وسياسات الغلق من مكان واحد.",
            "بيانات الشركة",
            "مواقع العمل والفروع",
            "سياسات الغلق",
            "{0} موظف فعال،",
            "{0} موقع عمل فعال،",
            "{0} قسم فعال."
        };

        Assert.All(requiredKeys, key => Assert.True(english.ContainsKey(key), $"Missing English key: {key}"));
        Assert.All(requiredKeys, key => Assert.True(kurdish.ContainsKey(key), $"Missing Kurdish key: {key}"));
        Assert.DoesNotContain(requiredKeys, key => string.Equals(english[key], key, StringComparison.Ordinal));
        Assert.DoesNotContain(requiredKeys, key => string.Equals(kurdish[key], key, StringComparison.Ordinal));

        var setup = ReadWeb("Pages", "Setup", "Index.cshtml");
        Assert.Contains("ViewData[\"Title\"] = T[\"إعداد الشركة\"]", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserMenu_OpensIntoTheContentAreaInEnglish()
    {
        var menu = ReadWeb("wwwroot", "css", "zynora-user-menu.css");
        var layout = ReadWeb("Pages", "Shared", "_Layout.cshtml");
        var login = ReadWeb("Pages", "Account", "Login.cshtml");

        Assert.Contains("html[dir=\"ltr\"] .zy-user-menu__panel", menu, StringComparison.Ordinal);
        Assert.Contains("inset-inline-start: 0", menu, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end: auto", menu, StringComparison.Ordinal);
        Assert.Contains("data-zy-no-localize", layout, StringComparison.Ordinal);
        Assert.Contains("data-zy-no-localize", login, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishNavigation_ReversesThePhysicalShellAndDrawerPlacement()
    {
        var refresh = ReadWeb("wwwroot", "css", "zynora-refresh-2026.css");
        var navigation = ReadWeb("wwwroot", "js", "zynora-kayan-nav.js");

        Assert.Contains("html[dir=\"ltr\"] .nexora-shell", refresh, StringComparison.Ordinal);
        Assert.Contains("html[dir=\"ltr\"] .nexora-sidebar", refresh, StringComparison.Ordinal);
        Assert.Contains("html[dir=\"ltr\"] .nexora-main", refresh, StringComparison.Ordinal);
        Assert.Contains("--ky-left", refresh, StringComparison.Ordinal);
        Assert.Contains("kySlideOverLtr", refresh, StringComparison.Ordinal);

        Assert.Contains("links.style.setProperty(\"--ky-left\"", navigation, StringComparison.Ordinal);
        Assert.Contains("return \"Back\"", navigation, StringComparison.Ordinal);
        Assert.Contains("return \"گەڕانەوە\"", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("back.innerHTML", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginPasswordVisibilityToggle_IsAccessibleAndDoesNotAlterThePasswordValue()
    {
        var login = ReadWeb("Pages", "Account", "Login.cshtml");

        Assert.Contains("id=\"password-visibility-toggle\"", login, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"Password\"", login, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", login, StringComparison.Ordinal);
        Assert.Contains("input.type = willShow ? \"text\" : \"password\"", login, StringComparison.Ordinal);
        Assert.Contains("toggle.dataset.hideLabel", login, StringComparison.Ordinal);
        Assert.Contains("@T[Model.ErrorMessage]", login, StringComparison.Ordinal);
        Assert.DoesNotContain("input.value =", login, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", login, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BiometricLogin_IsHiddenInNormalBrowserTabsAndLimitedToInstalledAppMode()
    {
        var login = ReadWeb("Pages", "Account", "Login.cshtml");
        var refresh = ReadWeb("wwwroot", "css", "zynora-refresh-2026.css");
        var appModeGate = login.IndexOf("if (!isInstalledAppMode || !window.PublicKeyCredential) return;", StringComparison.Ordinal);
        var revealButton = login.IndexOf("btn.hidden = false;", StringComparison.Ordinal);

        Assert.Contains("id=\"bio-login-btn\"", login, StringComparison.Ordinal);
        Assert.Contains("id=\"bio-login-btn\" class=\"login-button zyu-8d3380c010b4\" hidden", login, StringComparison.Ordinal);
        Assert.Contains("window.matchMedia(\"(display-mode: standalone)\").matches", login, StringComparison.Ordinal);
        Assert.Contains("window.navigator.standalone === true", login, StringComparison.Ordinal);
        Assert.True(appModeGate >= 0, "Expected a normal-browser gate before biometric login is revealed.");
        Assert.True(revealButton > appModeGate, "Biometric login must only be revealed after installed-app mode is verified.");
        Assert.Contains("#bio-login-btn[hidden]", refresh, StringComparison.Ordinal);
        Assert.Contains("#bio-login-error[hidden]", refresh, StringComparison.Ordinal);
        Assert.Contains("display: none !important", refresh, StringComparison.Ordinal);
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
