using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class MobileLocalizationContractTests
{
    private static readonly Regex QuotedString =
        new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static readonly Regex ArabicScript =
        new("[\\u0600-\\u06FF]", RegexOptions.Compiled);

    private static readonly Regex Placeholder =
        new("\\{[^{}]+\\}", RegexOptions.Compiled);

    [Fact]
    public void Catalogs_CoverAllMobileArabicLiterals_WithMatchingKeys()
    {
        var root = FindRepoRoot();
        var mobile = Path.Combine(root, "ZynoraHR.Mobile");
        var localization = Path.Combine(mobile, "Resources", "Localization");

        var ar = Load(Path.Combine(localization, "ui.ar.json"));
        var en = Load(Path.Combine(localization, "ui.en.json"));
        var ckb = Load(Path.Combine(localization, "ui.ckb.json"));
        Assert.Equal(ar.Keys.Order(), en.Keys.Order());
        Assert.Equal(ar.Keys.Order(), ckb.Keys.Order());

        var source = Directory
            .EnumerateFiles(mobile, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => ExtractArabicLiterals(File.ReadAllText(path)))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            source.Order(StringComparer.Ordinal),
            ar.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalogs_PreservePlaceholders_AndEnglishHasNoArabicScript()
    {
        var root = FindRepoRoot();
        var localization = Path.Combine(
            root, "ZynoraHR.Mobile", "Resources", "Localization");

        var ar = Load(Path.Combine(localization, "ui.ar.json"));
        var en = Load(Path.Combine(localization, "ui.en.json"));
        var ckb = Load(Path.Combine(localization, "ui.ckb.json"));
        foreach (var key in ar.Keys)
        {
            var expected = Placeholder.Matches(key)
                .Select(match => match.Value)
                .ToArray();

            Assert.Equal(
                expected,
                Placeholder.Matches(ar[key]).Select(match => match.Value).ToArray());
            Assert.Equal(
                expected,
                Placeholder.Matches(en[key]).Select(match => match.Value).ToArray());
            Assert.Equal(
                expected,
                Placeholder.Matches(ckb[key]).Select(match => match.Value).ToArray());

            Assert.False(
                ArabicScript.IsMatch(en[key]),
                $"English translation contains Arabic script for key: {key}");
        }

        Assert.DoesNotContain(
            en,
            pair => pair.Key.Contains("قريباً", StringComparison.Ordinal) ||
                    pair.Value.Contains("قريباً", StringComparison.Ordinal));
        Assert.DoesNotContain(
            ckb,
            pair => pair.Key.Contains("قريباً", StringComparison.Ordinal) ||
                    pair.Value.Contains("قريباً", StringComparison.Ordinal));
    }
    private static Dictionary<string, string> Load(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Invalid catalog: {path}");

    private static IEnumerable<string> ExtractArabicLiterals(string text)
    {
        foreach (Match match in QuotedString.Matches(text))
        {
            var value = match.Groups[1].Value;
            if (ArabicScript.IsMatch(value))
                yield return value;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
