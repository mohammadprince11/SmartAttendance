using System.Text.Json;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public sealed class PollTextTranslation
{
    public string LanguageCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Question { get; set; } = "";
    public string[] Options { get; set; } = [];
}

public sealed record PollTranslationDocument(string PrimaryLanguage, List<PollTextTranslation> Translations);

public static class PollTranslations
{
    public static string Normalize(string? primary, IEnumerable<PollTextTranslation>? translations, IEnumerable<string> allowed, int optionCount)
    {
        var languages = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(primary) || !languages.Contains(primary))
            throw new ArgumentException("اختر لغة أساسية من لغات النظام.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primary };
        var result = new List<PollTextTranslation>();
        foreach (var text in translations ?? [])
        {
            var code = text.LanguageCode?.Trim() ?? "";
            var options = (text.Options ?? []).Select(x => x?.Trim() ?? "").ToArray();
            var title = text.Title?.Trim() ?? "";
            var question = text.Question?.Trim() ?? "";
            if (!languages.Contains(code) || !seen.Add(code))
                throw new ArgumentException("اختر لغة متاحة وغير مكررة للترجمة.");
            if (title.Length is < 1 or > 250 || question.Length is < 1 or > 4000 ||
                options.Length != optionCount || options.Any(x => x.Length is < 1 or > 300) ||
                options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != optionCount)
                throw new ArgumentException("أكمل عنوان وسؤال وجميع خيارات كل لغة، بدون تكرار الخيارات.");
            result.Add(new() { LanguageCode = code, Title = title, Question = question, Options = options });
        }
        return JsonSerializer.Serialize(new PollTranslationDocument(primary, result));
    }

    public static PollTextTranslation? Resolve(string? json, string culture, int optionCount)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var document = JsonSerializer.Deserialize<PollTranslationDocument>(json);
            if (document == null) return null;
            var primary = document.PrimaryLanguage;
            // Prefer the entire primary content to a mixed-language poll when no complete translation exists.
            if (Matches(primary, culture)) return null;
            var text = document.Translations?.FirstOrDefault(t => t != null && Matches(t.LanguageCode, culture));
            return text != null && !string.IsNullOrWhiteSpace(text.Title) && !string.IsNullOrWhiteSpace(text.Question) &&
                text.Options?.Length == optionCount && text.Options.All(x => !string.IsNullOrWhiteSpace(x)) ? text : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool Matches(string? code, string culture) =>
        string.Equals(code, culture, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(code) && code.Split('-')[0].Equals(culture.Split('-')[0], StringComparison.OrdinalIgnoreCase));
}
