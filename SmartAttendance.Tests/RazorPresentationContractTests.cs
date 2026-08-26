using System.Text.RegularExpressions;

namespace SmartAttendance.Tests;

public sealed class RazorPresentationContractTests
{
    [Fact]
    public void RazorPages_ContainNoEmbeddedStyleBlocksOrStyleAttributes()
    {
        var pages = Path.Combine(RepoRoot(), "SmartAttendance.Web", "Pages");
        var violations = Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, number = index + 1 }))
            .Where(row => Regex.IsMatch(row.line, @"<style\b|<[^>]*\sstyle\s*=", RegexOptions.IgnoreCase))
            .Select(row => $"{Path.GetRelativePath(RepoRoot(), row.path)}:{row.number}")
            .ToArray();

        Assert.True(violations.Length == 0,
            "CSS must live outside Razor pages. Violations: " + string.Join(", ", violations));
    }

    [Fact]
    public void MigratedPageCss_UsesTokensAndLogicalDirectionProperties()
    {
        var directory = Path.Combine(RepoRoot(), "SmartAttendance.Web", "wwwroot", "css", "pages");
        var css = string.Join("\n", Directory.EnumerateFiles(directory, "*.css").Select(File.ReadAllText));

        Assert.DoesNotMatch(new Regex(@"#[0-9a-f]{3,8}\b|rgba?\(", RegexOptions.IgnoreCase), css);
        Assert.DoesNotMatch(new Regex(@"(?<![-\w])(left|right)\s*:", RegexOptions.IgnoreCase), css);
        Assert.DoesNotMatch(new Regex(@"\b(margin|padding|border)-(left|right)\b", RegexOptions.IgnoreCase), css);
        Assert.DoesNotMatch(new Regex(@"text-align\s*:\s*(left|right)\b", RegexOptions.IgnoreCase), css);
    }

    [Fact]
    public void DynamicStyleBridge_IsAllowListedAndRejectsExecutableCss()
    {
        var bridge = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "wwwroot", "js", "zynora-dynamic-style.js"));

        Assert.Contains("var allowed = new Set", bridge);
        Assert.Contains("url\\s*\\(", bridge);
        Assert.Contains("javascript:", bridge);
        Assert.Contains("element.style.setProperty", bridge);
        Assert.DoesNotContain("cssText =", bridge);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
