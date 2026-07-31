namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// حساب تباين WCAG بين لونين — أداة قياس لا عرض.
///
/// وجودها بالكود لا بورقة جانبية مقصود: ألوان الهوية تُعدَّل بكل جلسة تصميم،
/// و«مريح للعين» حكم لا يُدافَع عنه بالذوق. الاختبارات تثبّت الحدود فيُكشف أي
/// انحدار لحظة حدوثه بدل اكتشافه بعد أن يشتكي مستخدم من إجهاد بجلسة ساعات.
///
/// الحدود (WCAG 2.1): نص عادي 4.5:1 · نص كبير وحدود العناصر التفاعلية 3:1.
/// </summary>
public static class ColorContrast
{
    public const double NormalTextThreshold = 4.5;
    public const double LargeTextThreshold = 3.0;

    /// <summary>نسبة التباين بين لونين ست‌عشريين (‎#rrggbb‎). النتيجة بين 1 و21.</summary>
    public static double Ratio(string foreground, string background)
    {
        var lighter = Luminance(foreground);
        var darker = Luminance(background);

        if (darker > lighter)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    public static bool MeetsNormalText(string foreground, string background) =>
        Ratio(foreground, background) >= NormalTextThreshold;

    public static bool MeetsLargeTextOrBorder(string foreground, string background) =>
        Ratio(foreground, background) >= LargeTextThreshold;

    /// <summary>الإضاءة النسبية بحسب WCAG (تصحيح غاما لكل قناة).</summary>
    public static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(int value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static (int R, int G, int B) Parse(string hex)
    {
        var value = (hex ?? string.Empty).Trim().TrimStart('#');

        // الصيغة المختصرة (#abc) تُوسَّع بتكرار كل محرف.
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        if (value.Length != 6 || !int.TryParse(
                value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var packed))
        {
            throw new ArgumentException($"لون غير صالح: '{hex}'", nameof(hex));
        }

        return ((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);
    }
}
