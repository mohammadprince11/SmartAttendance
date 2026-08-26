using System.Text.RegularExpressions;

namespace SmartAttendance.Tests;

internal static class RazorPageAssetReader
{
    public static string ReadWithLinkedPageCss(params string[] pageParts)
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(new[] { root, "SmartAttendance.Web", "Pages" }
            .Concat(pageParts).ToArray()));
        var css = Regex.Matches(page, "href=\"~/css/pages/(?<name>[^\"]+\\.css)\"")
            .Select(match => File.ReadAllText(Path.Combine(
                root, "SmartAttendance.Web", "wwwroot", "css", "pages", match.Groups["name"].Value)));
        return page + Environment.NewLine + string.Join(Environment.NewLine, css);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
