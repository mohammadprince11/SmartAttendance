namespace SmartAttendance.Web.Infrastructure.Localization;

public static class CompanyLanguagePolicy
{
    public static string? ValidateSelection(
        string defaultCultureCode,
        IReadOnlyCollection<string> activeCultureCodes)
    {
        var active = activeCultureCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (active.Length == 0)
            return "يجب تفعيل لغة واحدة على الأقل لبيانات الشركة.";
        if (!active.Contains(defaultCultureCode, StringComparer.OrdinalIgnoreCase))
            return "اللغة الأساسية يجب أن تكون ضمن اللغات المفعلة.";
        return null;
    }

    public static IReadOnlyList<string> MissingRequiredValues(
        IReadOnlyCollection<CompanyLanguageOption> languages,
        IReadOnlyCollection<string> fieldNames,
        IReadOnlyDictionary<(string CultureCode, string FieldName), string> values)
    {
        var errors = new List<string>();
        foreach (var field in fieldNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var language in languages.Where(item => item.IsRequired))
            {
                var match = values.FirstOrDefault(pair =>
                    string.Equals(pair.Key.CultureCode, language.CultureCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pair.Key.FieldName, field, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(match.Value))
                    errors.Add($"الحقل {field} مطلوب باللغة {language.NativeName}.");
            }
        }
        return errors;
    }
}
