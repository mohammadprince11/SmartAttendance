using Xunit;

namespace SmartAttendance.Tests;

public sealed class RuntimeLocalizationBridgeCoverageContractTests
{
    [Fact]
    public void Bridge_LocalizesTextareaUiAttributesWithoutTouchingTextareaContent()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js"));

        Assert.Contains(
            "var ignoredElements = new Set([\"SCRIPT\", \"STYLE\", \"NOSCRIPT\", \"CODE\", \"PRE\"]);",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "var ignoredTextParents = new Set([\"SCRIPT\", \"STYLE\", \"NOSCRIPT\", \"TEXTAREA\", \"CODE\", \"PRE\"]);",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "ignoredTextParents.has(element.tagName)",
            script,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "ignoredElements = new Set([\"SCRIPT\", \"STYLE\", \"NOSCRIPT\", \"TEXTAREA\"",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"aria-description\"")]
    [InlineData("\"alt\"")]
    [InlineData("\"data-title\"")]
    [InlineData("\"data-tooltip\"")]
    [InlineData("\"data-original-title\"")]
    [InlineData("\"data-bs-original-title\"")]
    public void Bridge_CoversAuditVisibleAttributes(string attribute)
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js"));

        Assert.Contains(
            attribute,
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneShell_UsesTheP2RuntimeBridgeVersion()
    {
        var root = FindRoot();
        var tagHelpers = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Localization",
            "LocalizationShellTagHelpers.cs"));

        Assert.Contains(
            "/js/zynora-runtime-localization.js?v=20260906-p2",
            tagHelpers,
            StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}