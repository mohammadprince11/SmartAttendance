namespace SmartAttendance.Tests;

public sealed class DropdownUnificationContractTests
{
    [Fact]
    public void EveryApplicationLayout_LoadsTheSameDropdownContract()
    {
        foreach (var layout in new[] { "_Layout.cshtml", "_EmployeePortalLayout.cshtml", "_PlatformLayout.cshtml" })
        {
            var source = ReadWeb("Pages", "Shared", layout);

            Assert.Contains("~/css/zynora-select-system.css", source, StringComparison.Ordinal);
            Assert.Contains("~/css/zynora-dropdown-contract.css", source, StringComparison.Ordinal);
            Assert.Contains("~/js/zynora-select-system.js", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DropdownContract_CoversGeneratedSearchablePortalAndNativeMultiSelects()
    {
        var source = ReadWeb("wwwroot", "css", "zynora-dropdown-contract.css");

        Assert.Contains(".nxcs-trigger.nxcs-trigger", source, StringComparison.Ordinal);
        Assert.Contains(".nxcs-panel.nxcs-panel", source, StringComparison.Ordinal);
        Assert.Contains(".nxcs-option.nxcs-option", source, StringComparison.Ordinal);
        Assert.Contains(".nx-search-select__button", source, StringComparison.Ordinal);
        Assert.Contains(".nxex-csel-trigger", source, StringComparison.Ordinal);
        Assert.Contains("select[multiple]", source, StringComparison.Ordinal);
        Assert.Contains("html[data-theme=\"light\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DropdownPalette_UsesIdentityTokensSharedByAllLayouts()
    {
        var source = ReadWeb("wwwroot", "css", "zynora-dropdown-contract.css");
        Assert.Contains("--zy-dd-field: #111b2a", source);
        Assert.Contains("--zy-dd-panel: #0f1a29", source);
        Assert.Contains("--zy-dd-option-active: #20374f", source);
        Assert.Contains("@layer zynora-dropdown-contract {", source);
        Assert.Contains(".nxr-search-dropdown .nxr-option", source);
        Assert.Contains("--zy-dd-text: var(--text-default)", source);
        Assert.Contains("--zy-dd-muted: var(--text-muted)", source);
        Assert.DoesNotContain("var(--color-text-primary", source);
        Assert.DoesNotContain("var(--color-text-secondary", source);
        Assert.Contains("scrollbar-color: var(--zy-dd-border-focus) var(--zy-dd-panel)", source);
        Assert.Contains("font-family: var(--zy-dd-font) !important", source);
    }

    [Fact]
    public void OnboardingReview_DoesNotOverrideEnhancedSelectPalette()
    {
        var source = ReadWeb("wwwroot", "css", "pages", "smart-onboarding-review.css");
        Assert.DoesNotContain(".nxcs-trigger", source);
    }

    [Fact]
    public void EmployeePortal_DoesNotLoadASecondPageLocalSelectSystem()
    {
        var page = ReadWeb("Pages", "EmployeePortal", "DataChange.cshtml");

        Assert.DoesNotContain("zynora-select-system.css", page, StringComparison.Ordinal);
        Assert.DoesNotContain("zynora-select-system.js", page, StringComparison.Ordinal);
    }

    private static string ReadWeb(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root, "SmartAttendance.Web" }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
