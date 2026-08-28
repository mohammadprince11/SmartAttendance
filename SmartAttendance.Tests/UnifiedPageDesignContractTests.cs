using System.Text.RegularExpressions;

namespace SmartAttendance.Tests;

public sealed class UnifiedPageDesignContractTests
{
    [Fact]
    public void ApplicationShells_LoadTheCanonicalContractLast()
    {
        foreach (var layoutName in new[] { "_Layout.cshtml", "_EmployeePortalLayout.cshtml" })
        {
            var layout = ReadWeb("Pages", "Shared", layoutName);
            var finalCss = layout.LastIndexOf("zynora-unified-pages.css", StringComparison.Ordinal);
            var adapter = layout.LastIndexOf("zynora-unified-pages.js", StringComparison.Ordinal);

            Assert.Contains("zy-app", layout, StringComparison.Ordinal);
            Assert.Contains("zy-scope zy-ui-contract", layout, StringComparison.Ordinal);
            Assert.True(finalCss > layout.LastIndexOf("@RenderBody", StringComparison.Ordinal),
                $"{layoutName} must load the final page contract after page-specific styles.");
            Assert.True(adapter > finalCss, $"{layoutName} must load its adapter after the final CSS contract.");
        }
    }

    [Fact]
    public void StandaloneHtmlPages_AreCoveredWithoutChangingPrintableDocuments()
    {
        var pagesRoot = Path.Combine(WebRoot(), "Pages");
        var uncovered = Directory.EnumerateFiles(pagesRoot, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("@page", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals("ThemeCss.cshtml", StringComparison.OrdinalIgnoreCase))
            // Culture endpoints only return JSON or a redirect from their PageModel;
            // their cshtml files are routing stubs and never render an HTML document.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Culture{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { path, source = File.ReadAllText(path) })
            .Where(page => Regex.IsMatch(page.source, @"Layout\s*=\s*null", RegexOptions.IgnoreCase))
            .Where(page => !page.source.Contains("zy-ui-contract", StringComparison.Ordinal)
                           || !page.source.Contains("zynora-unified-pages.css", StringComparison.Ordinal)
                           || !page.source.Contains("zynora-unified-pages.js", StringComparison.Ordinal))
            .Select(page => Path.GetRelativePath(RepoRoot(), page.path))
            .ToArray();

        Assert.True(uncovered.Length == 0,
            "Standalone HTML pages missing the unified contract: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void RuntimeAdapter_PreservesMarkupAndCoversCoreComponentFamilies()
    {
        var adapter = ReadWeb("wwwroot", "js", "zynora-unified-pages.js");

        Assert.Contains(".zy-ui-contract", adapter, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", adapter, StringComparison.Ordinal);
        Assert.Contains("classList.add", adapter, StringComparison.Ordinal);
        Assert.Contains("data-zy-preserve", adapter, StringComparison.Ordinal);
        Assert.Contains("isManagedWidget", adapter, StringComparison.Ordinal);
        Assert.Contains("nxcal", adapter, StringComparison.Ordinal);
        Assert.Contains("nxcs", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("className =", adapter, StringComparison.Ordinal);

        foreach (var canonicalClass in new[]
                 {
                     "zy-page-title", "zy-page-header", "zy-btn", "zy-input", "zy-select",
                     "zy-textarea", "zy-card", "zy-filter-bar", "zy-table", "zy-table-wrap",
                     "zy-badge", "zy-alert", "zy-tabs", "zy-pagination", "zy-empty"
                 })
        {
            Assert.Contains(canonicalClass, adapter, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FinalContract_UsesTokensLogicalPropertiesAndCanonicalSelectorsOnly()
    {
        var css = ReadWeb("wwwroot", "css", "zynora-unified-pages.css");
        var rules = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        foreach (var selector in new[]
                 {
                     ".zy-ui-contract .zy-btn", ".zy-ui-contract .zy-input",
                     ".zy-ui-contract .zy-card", ".zy-ui-contract .zy-table",
                     ".zy-ui-contract .zy-alert", ".zy-ui-contract .zy-empty"
                 })
        {
            Assert.Contains(selector, rules, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"#[0-9a-f]{3,8}\b|rgba?\(", RegexOptions.IgnoreCase), rules);
        Assert.DoesNotMatch(new Regex(@"(?<![-\w])(left|right)\s*:", RegexOptions.IgnoreCase), rules);
        Assert.DoesNotMatch(new Regex(@"\b(margin|padding|border)-(left|right)\b", RegexOptions.IgnoreCase), rules);
        Assert.DoesNotMatch(new Regex(@"\.(zhr|hrms|nxr|nxex|nexora|ps|ats|dat)-", RegexOptions.IgnoreCase), rules);
    }

    [Fact]
    public void PageCssFamilies_AreRatchetLocked()
    {
        var pageCss = Directory.EnumerateFiles(
            Path.Combine(WebRoot(), "wwwroot", "css", "pages"), "*.css", SearchOption.AllDirectories).Count();

        Assert.True(pageCss <= 122,
            $"Do not add another page-local design family; extend the canonical contract instead. Found {pageCss} files.");
    }

    [Fact]
    public void LightTheme_CoversDirectContractConsumersAndTheFullHeightModuleDrawer()
    {
        var themeContract = ReadWeb("wwwroot", "css", "zynora-theme-contract.css");
        var navigation = ReadWeb("wwwroot", "css", "zynora-kayan-nav.css");

        Assert.Contains("html[data-theme=\"light\"]", themeContract, StringComparison.Ordinal);
        foreach (var token in new[]
                 {
                     "--surface-app", "--surface-base", "--surface-panel", "--surface-raised",
                     "--surface-input", "--text-default", "--text-muted", "--border-default",
                     "--interactive-primary-soft", "--elevation-lg"
                 })
        {
            Assert.Contains(token, themeContract, StringComparison.Ordinal);
        }

        Assert.Contains("html[data-theme=\"light\"] .nexora-nav-group.ky-open > .nexora-nav-group-links",
            navigation, StringComparison.Ordinal);
        Assert.Contains("html[data-theme=\"light\"] .nexora-nav-group-links .ky-back",
            navigation, StringComparison.Ordinal);
        Assert.Contains("html[data-theme=\"light\"] .nexora-nav-group-links .ky-drawer-title",
            navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh2026_IsTheFinalVisualAuthorityAndLoginUsesTheNewBrandStage()
    {
        var layout = ReadWeb("Pages", "Shared", "_Layout.cshtml");
        var login = ReadWeb("Pages", "Account", "Login.cshtml");
        var refresh = ReadWeb("wwwroot", "css", "zynora-refresh-2026.css");

        Assert.True(
            layout.LastIndexOf("zynora-refresh-2026.css", StringComparison.Ordinal) >
            layout.LastIndexOf("zynora-direction.css", StringComparison.Ordinal),
            "The refresh stylesheet must remain the final visual authority.");
        Assert.Contains("data-theme=\"light\"", layout, StringComparison.Ordinal);
        Assert.Contains("refresh-2026-v1", layout, StringComparison.Ordinal);

        Assert.Contains("login-stage", login, StringComparison.Ordinal);
        Assert.Contains("login-visual", login, StringComparison.Ordinal);
        Assert.Contains("zynora-symbol.svg", login, StringComparison.Ordinal);

        foreach (var selector in new[]
                 {
                     ".nexora-sidebar", ".nexora-topbar", ".zy-ui-contract .zy-card",
                     ".zy-ui-contract .zy-table", ".login-stage", ".login-visual"
                 })
        {
            Assert.Contains(selector, refresh, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RazorImports_DoNotRegisterTheBroadRuntimeAdapterAsATagHelper()
    {
        var imports = ReadWeb("Pages", "_ViewImports.cshtml");
        Assert.DoesNotContain("@addTagHelper *, SmartAttendance.Web", imports, StringComparison.Ordinal);
    }

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { WebRoot() }.Concat(parts).ToArray()));

    private static string WebRoot() => Path.Combine(RepoRoot(), "SmartAttendance.Web");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
