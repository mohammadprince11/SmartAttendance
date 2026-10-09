namespace SmartAttendance.Application.Announcements.Models;

public sealed class FreeAnnouncementText
{
    public string? LanguageCode { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
}

public sealed record AnnouncementImageUpload(Guid Id, int CompanyId, string Name, string ContentType, byte[] Data);

public static class FreeAnnouncement
{
    public static List<StudioRendered> Normalize(IEnumerable<FreeAnnouncementText> inputs, string? primaryLanguage, IEnumerable<string> availableLanguages)
    {
        var allowed = availableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(primaryLanguage) || !allowed.Contains(primaryLanguage))
            throw new ArgumentException("اختر اللغة الأساسية من لغات النظام.");
        var result = new List<StudioRendered>();
        foreach (var input in inputs)
        {
            var title = input.Title?.Trim() ?? "";
            var body = input.Body?.Trim() ?? "";
            if (title.Length == 0 && body.Length == 0) continue;
            var code = input.LanguageCode?.Trim() ?? "";
            if (!allowed.Contains(code) || !AnnouncementStudio.ValidLanguage(code))
                throw new ArgumentException("اختر لغة ترجمة من لغات النظام.");
            if (result.Any(t => t.LanguageCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("رمز اللغة مكرر.");
            if (title.Length is < 1 or > 250 || body.Length is < 1 or > 20000)
                throw new ArgumentException("أكمل عنوان ونص كل ترجمة أضفتها. العنوان حتى 250 حرفاً والنص حتى 20000 حرف.");
            result.Add(new(code, title, body));
        }
        if (result.Count > 12) throw new ArgumentException("يمكن إضافة 12 لغة كحد أقصى للإعلان الواحد.");
        var primary = result.SingleOrDefault(t => t.LanguageCode.Equals(primaryLanguage, StringComparison.OrdinalIgnoreCase));
        if (primary == null) throw new ArgumentException("أكمل عنوان ونص الإعلان باللغة الأساسية.");
        result.Remove(primary); result.Insert(0, primary);
        return result;
    }
}
