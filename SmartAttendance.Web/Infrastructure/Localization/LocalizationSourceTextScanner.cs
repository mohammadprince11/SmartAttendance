using System.Net;
using System.Text.RegularExpressions;

namespace SmartAttendance.Web.Infrastructure.Localization;

/// <summary>
/// Discovers source-language text that is rendered by Razor pages or returned by
/// the web layer.  A large part of the legacy UI still relies on the runtime DOM
/// localization bridge, so those strings are not referenced through IStringLocalizer
/// and would otherwise be absent from the administrator dictionary.
/// </summary>
public static partial class LocalizationSourceTextScanner
{
    private const int MaxKeyLength = 4_000;

    public static IReadOnlyCollection<string> Scan(string contentRootPath)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(contentRootPath) || !Directory.Exists(contentRootPath))
            return keys;

        var pagesPath = Path.Combine(contentRootPath, "Pages");
        foreach (var file in EnumerateFiles(pagesPath, "*.cshtml"))
            ScanRazorFile(file, keys);

        foreach (var root in WebCodeRoots(contentRootPath))
            foreach (var file in EnumerateFiles(root, "*.cs"))
                ScanCodeFile(file, keys);

        var scriptsPath = Path.Combine(contentRootPath, "wwwroot", "js");
        foreach (var file in EnumerateFiles(scriptsPath, "*.js"))
            ScanCodeFile(file, keys);

        // Application and infrastructure validation messages can reach the UI.
        // Scan sibling projects in a source checkout; published applications still
        // retain complete Razor coverage and all compiled RESX keys.
        var solutionRoot = Directory.GetParent(contentRootPath)?.FullName;
        if (solutionRoot is not null)
        {
            foreach (var project in new[] { "SmartAttendance.Application", "SmartAttendance.Infrastructure" })
            {
                var projectPath = Path.Combine(solutionRoot, project);
                foreach (var file in EnumerateFiles(projectPath, "*.cs"))
                    ScanCodeFile(file, keys);
            }
        }

        return keys;
    }

    private static IEnumerable<string> WebCodeRoots(string contentRootPath)
    {
        foreach (var name in new[] { "Controllers", "Infrastructure", "Localization" })
        {
            var path = Path.Combine(contentRootPath, name);
            if (Directory.Exists(path)) yield return path;
        }

        // Page models contain validation and operation-result text.
        var pages = Path.Combine(contentRootPath, "Pages");
        if (Directory.Exists(pages)) yield return pages;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var file in files)
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Seeding/", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }

    private static void ScanRazorFile(string file, ISet<string> keys)
    {
        var source = ReadText(file);
        if (source.Length == 0) return;

        source = RazorCommentRegex().Replace(source, string.Empty);
        source = HtmlCommentRegex().Replace(source, string.Empty);

        foreach (Match match in ScriptBlockRegex().Matches(source))
            ScanCodeText(match.Groups[1].Value, keys);

        // Script and style bodies are not HTML text nodes. Keeping them in the
        // markup pass turns concatenated JavaScript into thousands of false keys.
        var visibleMarkup = ScriptOrStyleBlockRegex().Replace(source, string.Empty);

        foreach (Match match in LocalizerLiteralRegex().Matches(visibleMarkup))
            Add(keys, DecodeQuotedLiteral(match.Groups[1].Value), requireArabic: true);

        foreach (Match match in VisibleAttributeRegex().Matches(visibleMarkup))
            Add(keys, NormalizeMarkupText(match.Groups[3].Value), requireArabic: true);

        foreach (Match match in InputButtonValueRegex().Matches(visibleMarkup))
            Add(keys, NormalizeMarkupText(match.Groups[2].Value), requireArabic: true);

        foreach (Match match in TextNodeRegex().Matches(visibleMarkup))
            Add(keys, NormalizeMarkupText(match.Groups[1].Value), requireArabic: true);
    }

    private static void ScanCodeFile(string file, ISet<string> keys)
    {
        var source = ReadText(file);
        if (source.Length == 0) return;

        ScanCodeText(source, keys);
    }

    private static void ScanCodeText(string source, ISet<string> keys)
    {
        foreach (Match match in QuotedLiteralRegex().Matches(source))
            Add(keys, NormalizeCodeText(match.Groups[1].Value), requireArabic: true);

        foreach (Match match in SingleQuotedJavascriptLiteralRegex().Matches(source))
            Add(keys, NormalizeCodeText(match.Groups[1].Value), requireArabic: true);
    }

    private static string ReadText(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private static string NormalizeMarkupText(string value)
    {
        value = WebUtility.HtmlDecode(value);
        value = LocalizerExpressionRegex().Replace(value, match => DecodeQuotedLiteral(match.Groups[1].Value));

        var placeholder = 0;
        value = RazorExpressionRegex().Replace(value, _ => $"{{{placeholder++}}}");
        value = RazorDirectiveRegex().Replace(value, string.Empty);
        return NormalizeWhitespace(value);
    }

    private static string NormalizeCodeText(string value)
    {
        value = DecodeQuotedLiteral(value);
        var placeholder = 0;
        value = InterpolationRegex().Replace(value, _ => $"{{{placeholder++}}}");
        return NormalizeWhitespace(value);
    }

    private static string DecodeQuotedLiteral(string value)
    {
        try { return Regex.Unescape(value.Replace("\"\"", "\"", StringComparison.Ordinal)); }
        catch (ArgumentException) { return value; }
    }

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    private static void Add(ISet<string> keys, string candidate, bool requireArabic)
    {
        if (candidate.Length is 0 or > MaxKeyLength) return;
        if (requireArabic && !ArabicTextRegex().IsMatch(candidate)) return;
        if (!LetterRegex().IsMatch(candidate)) return;
        if (candidate.StartsWith("@", StringComparison.Ordinal) ||
            candidate.StartsWith("#", StringComparison.Ordinal) ||
            candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.StartsWith(")}", StringComparison.Ordinal) ||
            candidate.StartsWith("%", StringComparison.Ordinal) ||
            candidate.EndsWith("%", StringComparison.Ordinal) ||
            candidate.EndsWith("{", StringComparison.Ordinal) ||
            candidate.Contains(") {", StringComparison.Ordinal) ||
            candidate.Contains("asp-", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("=>", StringComparison.Ordinal) ||
            CodePunctuationRegex().IsMatch(candidate) ||
            CodeTokenRegex().IsMatch(candidate) ||
            MemberAccessRegex().IsMatch(candidate))
            return;

        keys.Add(candidate);
    }

    [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)]
    private static partial Regex RazorCommentRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<script\b[^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"<(?:script|style)\b[^>]*>.*?</(?:script|style)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleBlockRegex();

    [GeneratedRegex("""(?:@?T|Localizer)\s*\[\s*"((?:\\.|[^"\\])*)"\s*\]""")]
    private static partial Regex LocalizerLiteralRegex();

    [GeneratedRegex("""(?:@?T|Localizer)\s*\[\s*"((?:\\.|[^"\\])*)"\s*\]""")]
    private static partial Regex LocalizerExpressionRegex();

    [GeneratedRegex("""\b(placeholder|title|aria-label|alt|data-sidebar-label|data-ky-title)\s*=\s*(["'])(.*?)\2""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex VisibleAttributeRegex();

    [GeneratedRegex("""<input\b(?=[^>]*\btype\s*=\s*["'](?:submit|button|reset)["'])[^>]*\bvalue\s*=\s*(["'])(.*?)\1[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InputButtonValueRegex();

    [GeneratedRegex(@">([^<>]+)<", RegexOptions.Singleline)]
    private static partial Regex TextNodeRegex();

    [GeneratedRegex("(?<!\")\"((?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex QuotedLiteralRegex();

    [GeneratedRegex(@"'((?:\\.|[^'\\])*)'")]
    private static partial Regex SingleQuotedJavascriptLiteralRegex();

    [GeneratedRegex(@"@(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_.]*(?:\([^)]*\))?)")]
    private static partial Regex RazorExpressionRegex();

    [GeneratedRegex(@"@(if|else|foreach|for|while|switch|case|using|inject|model|page|section)\b[^\r\n{]*[{}]?")]
    private static partial Regex RazorDirectiveRegex();

    [GeneratedRegex(@"\{(?!\d+\})(?:[^{}]|\{[^{}]*\})+\}")]
    private static partial Regex InterpolationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\u0600-\u06ff\u0750-\u077f\u08a0-\u08ff]")]
    private static partial Regex ArabicTextRegex();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();

    [GeneratedRegex("[\\\"'`$+;=<>\\[\\]]")]
    private static partial Regex CodePunctuationRegex();

    [GeneratedRegex(@"\b(?:var|let|const|return|function|document|window|Model|Html|Raw|String|Math|Date|true|false|null|catch|await|new)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodeTokenRegex();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_]")]
    private static partial Regex MemberAccessRegex();
}
