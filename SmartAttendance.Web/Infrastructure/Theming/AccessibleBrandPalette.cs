namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// يشتقّ من لون علامة الشركة نسخاً **مقروءة** للنصوص والأزرار.
///
/// الثغرة التي يغلقها (اكتُشفت بالتشغيل الحيّ 2026-07-31): محرّك الهوية يبثّ لون
/// الشركة خاماً، وتوكنز النظام تستعمله نصاً — فشركة تختار تيلاً فاتحاً مثل
/// <c>#18C7BD</c> تحصل على نص بنسبة <b>2.11:1</b> على الأبيض. حارس
/// <c>PaletteContrastTests</c> لا يراها لأنه يفحص القيم الثابتة لا ما يحقنه المحرّك.
///
/// المبدأ: **لا نرفض اختيار الشركة ولا نغيّر علامتها** — يبقى اللون كما هو
/// للمساحات الكبيرة (شرائط، رسوم، خلفيات)، ونشتقّ منه درجة أعمق/أفتح تُستعمل
/// للنص والأزرار فقط، بأقلّ إزاحة تحقّق حدّ القراءة. لون العلامة يبقى معروفاً،
/// والقراءة تصير مضمونة أياً كان الاختيار.
/// </summary>
public static class AccessibleBrandPalette
{
    /// <summary>أقصى عدد خطوات مزج قبل التسليم بالأبيض/الأسود الصافي.</summary>
    private const int Steps = 40;

    /// <summary>
    /// يرجع أقرب نسخة من <paramref name="colorHex"/> تُقرأ فوق
    /// <paramref name="backgroundHex"/> بالحد المطلوب. إن كان اللون مقروءاً أصلاً
    /// يُرجع كما هو — بلا تعديل تجميلي لا داعي له.
    /// </summary>
    public static string EnsureReadableOn(
        string colorHex,
        string backgroundHex,
        double threshold = ColorContrast.NormalTextThreshold)
    {
        if (ColorContrast.Ratio(colorHex, backgroundHex) >= threshold)
        {
            return Normalize(colorHex);
        }

        // خلفية فاتحة ⟹ نُعتّم اللون؛ خلفية داكنة ⟹ نُفتّحه. المزج التدريجي يحافظ
        // على الصبغة (hue) فتبقى العلامة معروفة، بخلاف استبدال اللون كلياً.
        var target = ColorContrast.Luminance(backgroundHex) > 0.18 ? "#000000" : "#ffffff";

        for (var step = 1; step <= Steps; step++)
        {
            var candidate = Mix(colorHex, target, step / (double)Steps);

            if (ColorContrast.Ratio(candidate, backgroundHex) >= threshold)
            {
                return candidate;
            }
        }

        return target;
    }

    /// <summary>
    /// خلفية زرّ تحمل النص المعطى بأمان: تُعتَّم/تُفتَّح حتى يُقرأ الحبر فوقها.
    /// </summary>
    public static string EnsureCarriesInk(
        string backgroundHex,
        string inkHex,
        double threshold = ColorContrast.NormalTextThreshold)
    {
        if (ColorContrast.Ratio(inkHex, backgroundHex) >= threshold)
        {
            return Normalize(backgroundHex);
        }

        // الحبر فاتح ⟹ نُعتّم الخلفية، والعكس بالعكس.
        var target = ColorContrast.Luminance(inkHex) > 0.5 ? "#000000" : "#ffffff";

        for (var step = 1; step <= Steps; step++)
        {
            var candidate = Mix(backgroundHex, target, step / (double)Steps);

            if (ColorContrast.Ratio(inkHex, candidate) >= threshold)
            {
                return candidate;
            }
        }

        return target;
    }

    /// <summary>أي الحبرين يُقرأ فوق اللون: الأبيض أم الحبر الداكن.</summary>
    public static string PickInk(string backgroundHex, string lightInk = "#ffffff", string darkInk = "#08303a") =>
        ColorContrast.Ratio(lightInk, backgroundHex) >= ColorContrast.Ratio(darkInk, backgroundHex)
            ? Normalize(lightInk)
            : Normalize(darkInk);

    /// <summary>مزج خطّي بين لونين (0 = الأول، 1 = الثاني).</summary>
    public static string Mix(string fromHex, string toHex, double amount)
    {
        var (r1, g1, b1) = Parse(fromHex);
        var (r2, g2, b2) = Parse(toHex);
        var t = Math.Clamp(amount, 0, 1);

        return string.Create(7, (r1, g1, b1, r2, g2, b2, t), (span, state) =>
        {
            span[0] = '#';
            Write(span.Slice(1, 2), Blend(state.r1, state.r2, state.t));
            Write(span.Slice(3, 2), Blend(state.g1, state.g2, state.t));
            Write(span.Slice(5, 2), Blend(state.b1, state.b2, state.t));
        });

        static int Blend(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);

        static void Write(Span<char> destination, int value)
        {
            const string digits = "0123456789abcdef";
            var clamped = Math.Clamp(value, 0, 255);
            destination[0] = digits[clamped >> 4];
            destination[1] = digits[clamped & 0xF];
        }
    }

    private static string Normalize(string hex)
    {
        var (r, g, b) = Parse(hex);
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static (int R, int G, int B) Parse(string hex)
    {
        var value = (hex ?? string.Empty).Trim().TrimStart('#');

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
