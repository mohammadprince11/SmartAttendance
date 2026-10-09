using SmartAttendance.Application.Announcements.Models;

namespace SmartAttendance.Web.Infrastructure.Localization;

/// <summary>Only public built-in template text uses the global UI dictionary. Company-authored content stays company-owned.</summary>
public static class AnnouncementTemplateDictionary
{
    public static bool Apply(StudioTemplate template, string code, IReadOnlyDictionary<string,string> catalog)
    {
        if (template.Languages.ContainsKey(code) || template.Languages.Count >= 12 ||
            !AnnouncementStudio.ValidLanguage(code) || !template.Languages.TryGetValue("ar", out var source)) return false;
        if (!catalog.TryGetValue(source.Title, out var title) || string.IsNullOrWhiteSpace(title) || title == source.Title ||
            !catalog.TryGetValue(source.Body, out var body) || string.IsNullOrWhiteSpace(body) || body == source.Body) return false;
        template.Languages[code] = new(title, body);
        if (AnnouncementStudio.Validate(template) == null) return true;
        template.Languages.Remove(code); // Invalid translated placeholders never break the original template.
        return false;
    }
}
