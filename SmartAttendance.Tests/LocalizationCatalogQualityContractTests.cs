using Xunit;

namespace SmartAttendance.Tests;

public sealed class LocalizationCatalogQualityContractTests
{
    [Fact]
    public void DictionaryService_RejectsArabicRemnantOverridesForLtrTargets()
    {
        var root = FindRoot();

        var source = Read(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Localization",
            "LocalizationDictionaryService.cs");

        Assert.Contains(
            "IsContaminatedLtrOverride",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "language.Direction",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"ltr\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "ContainsArabicScript(sourceKey)",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "ContainsArabicScript(translation)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeBridge_RejectsContaminatedLtrTranslations()
    {
        var root = FindRoot();

        var source = Read(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js");

        Assert.Contains(
            "function isUsableTranslation",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "targetDirection === \"ltr\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "arabicText.test(source)",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "arabicText.test(translated)",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "isUsableCatalogEntry(key)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeBridge_HasUniqueWhitespaceNormalizedExactLookup()
    {
        var root = FindRoot();

        var source = Read(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js");

        Assert.Contains(
            "normalizedCatalogKeys",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "function normalizeLookupKey",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            ".replace(/\\s+/g, \" \")",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "normalizedCatalogKeys[normalized] = null",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "getExactCatalogTranslation",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeBridge_UsesP5VersionMarker()
    {
        var root = FindRoot();

        var runtime = Read(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js");

        var shell = Read(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Localization",
            "LocalizationShellTagHelpers.cs");

        Assert.Contains(
            "20260907-p5",
            runtime,
            StringComparison.Ordinal);

        Assert.Contains(
            "/js/zynora-runtime-localization.js?v=20260907-p5",
            shell,
            StringComparison.Ordinal);
    }

    private static string Read(
        string root,
        params string[] parts) =>
        File.ReadAllText(
            Path.Combine(
                new[] { root }
                    .Concat(parts)
                    .ToArray()));

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