using System.Globalization;

namespace SmartAttendance.Web;

public sealed record ZynoraCulture(
    string Code,
    string NativeName,
    string EnglishName,
    CultureInfo Culture)
{
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;
}

/// <summary>
/// Single source of truth for cultures the product advertises as supported.
/// </summary>
public static class ZynoraSupportedCultures
{
    public const string DefaultCode = "ar-IQ";

    public static IReadOnlyList<ZynoraCulture> All { get; } =
    [
        Create("ar-IQ", "العربية", "Arabic"),
        Create("en-US", "English", "English"),
        Create("ckb-IQ", "کوردی", "Kurdish (Sorani)")
    ];

    public static bool TryGet(string? code, out ZynoraCulture culture)
    {
        var match = All.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            culture = null!;
            return false;
        }

        culture = match;
        return true;
    }

    private static ZynoraCulture Create(string code, string nativeName, string englishName)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        return new ZynoraCulture(code, nativeName, englishName, culture);
    }
}
