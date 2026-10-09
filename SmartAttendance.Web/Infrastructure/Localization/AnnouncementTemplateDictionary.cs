using SmartAttendance.Application.Announcements.Models;

namespace SmartAttendance.Web.Infrastructure.Localization;

/// <summary>Only public built-in template text uses the global UI dictionary. Company-authored content stays company-owned.</summary>
public static class AnnouncementTemplateDictionary
{
    public static bool Apply(StudioTemplate template, string code, IReadOnlyDictionary<string,string> catalog)
    {
        if (!AnnouncementStudio.ValidLanguage(code)) return false;
        // Look up immutable source keys: applying an Arabic override must not change
        // which dictionary keys subsequent languages use.
        var source = AnnouncementStudio.Defaults().FirstOrDefault(t => t.Key == template.Key)?.Languages["ar"];
        if (source == null) return false;
        var target = template.Languages.Keys.FirstOrDefault(k => string.Equals(k, code, StringComparison.OrdinalIgnoreCase))
            ?? template.Languages.Keys.FirstOrDefault(k => !k.Contains('-') && string.Equals(k, code.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            ?? code;
        var hadPrevious = template.Languages.TryGetValue(target, out var previous);
        if (!hadPrevious && template.Languages.Count >= 12) return false;
        if (!catalog.TryGetValue(source.Title, out var title) || string.IsNullOrWhiteSpace(title) || title == source.Title ||
            !catalog.TryGetValue(source.Body, out var body) || string.IsNullOrWhiteSpace(body) || body == source.Body) return false;
        template.Languages[target] = new(title, body);
        if (AnnouncementStudio.Validate(template) == null) return true;
        if (hadPrevious) template.Languages[target] = previous!;
        else template.Languages.Remove(target); // Invalid translations never break the original template.
        return false;
    }
}
