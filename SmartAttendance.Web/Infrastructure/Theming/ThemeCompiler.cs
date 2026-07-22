using System.Globalization;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Pure, deterministic compiler that turns a company's brand colours into a
/// <c>:root</c> override for the Theme Contract (zynora-theme-contract.css).
/// Runs at publish time (not per request); its output is stored immutably with
/// the version. It only emits brand-derived tokens — surfaces and status colours
/// stay on the ZYNORA Default so semantics remain brand-independent by design.
/// No user-authored CSS is ever passed through; every value is machine-derived.
/// </summary>
public static class ThemeCompiler
{
    /// <summary>Brand input captured from the Branding Studio (P6). Hex like #18C7BD.</summary>
    public sealed record BrandingInput(
        string PrimaryHex,
        string? SecondaryHex = null,
        string? AccentHex = null);

    public enum ValidationLevel
    {
        Pass,
        Warn,
        Block,
    }

    public sealed record CompileResult(
        string CompiledCss,
        ValidationLevel Level,
        IReadOnlyList<string> Messages,
        string NormalizedPrimaryHex,
        string OnPrimaryHex);

    // Dark surfaces the compiled brand sits on; used to validate readability of
    // the brand colour against the app background. Mirrors the ZYNORA Default.
    private const string SurfaceAppHex = "#081426";
    private const string SurfacePanelHex = "#12223A";

    public static CompileResult Compile(BrandingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!TryParseHex(input.PrimaryHex, out var primary))
        {
            return new CompileResult(
                string.Empty,
                ValidationLevel.Block,
                new[] { $"لون رئيسي غير صالح: '{input.PrimaryHex}'." },
                string.Empty,
                string.Empty);
        }

        var primaryHsl = RgbToHsl(primary);
        var messages = new List<string>();

        // Lighter primary (aqua) and hover: on a dark shell, hover lightens.
        var aqua = HslToRgb(primaryHsl with { L = ClampL(primaryHsl.L + 0.12) });
        var hover = HslToRgb(primaryHsl with { L = ClampL(primaryHsl.L + 0.08) });

        // on-primary: pick black or white for best contrast on the brand colour.
        var onPrimary = ContrastRatio(primary, Rgb.White) >= ContrastRatio(primary, Rgb.Black)
            ? Rgb.White
            : Rgb.Black;
        var onPrimaryRatio = ContrastRatio(primary, onPrimary);

        Rgb? accent = TryParseHex(input.AccentHex, out var accentRgb) ? accentRgb : null;
        Rgb? secondary = TryParseHex(input.SecondaryHex, out var secondaryRgb) ? secondaryRgb : null;

        // Validation: readability of the primary as an accent on dark surfaces,
        // and legibility of text placed on the primary.
        var primaryOnApp = ContrastRatio(primary, ParseKnownHex(SurfaceAppHex));
        var primaryOnPanel = ContrastRatio(primary, ParseKnownHex(SurfacePanelHex));
        var level = ValidationLevel.Pass;

        if (primaryOnApp < 3.0 && primaryOnPanel < 3.0)
        {
            level = ValidationLevel.Block;
            messages.Add(
                $"اللون الرئيسي لا يظهر بوضوح على الخلفية الداكنة (تباين {primaryOnPanel:0.0}:1، الحد 3:1).");
        }
        else if (primaryOnApp < 3.0)
        {
            level = ValidationLevel.Warn;
            messages.Add(
                $"اللون الرئيسي تباينه منخفض على أغمق خلفية (تباين {primaryOnApp:0.0}:1).");
        }

        if (onPrimaryRatio < 4.5)
        {
            // Text on the brand colour is hard to read even with best of black/white.
            if (level != ValidationLevel.Block)
            {
                level = ValidationLevel.Warn;
            }
            messages.Add(
                $"النص فوق اللون الرئيسي تباينه {onPrimaryRatio:0.0}:1 (المفضل 4.5:1).");
        }

        if (level == ValidationLevel.Pass)
        {
            messages.Add("اجتاز فحوص التباين.");
        }

        var css = level == ValidationLevel.Block
            ? string.Empty
            : BuildCss(primary, primaryHsl, aqua, hover, onPrimary, accent, secondary);

        return new CompileResult(
            css,
            level,
            messages,
            ToHex(primary),
            ToHex(onPrimary));
    }

    private static string BuildCss(
        Rgb primary,
        Hsl primaryHsl,
        Rgb aqua,
        Rgb hover,
        Rgb onPrimary,
        Rgb? accent,
        Rgb? secondary)
    {
        var sb = new StringBuilder();
        sb.Append(":root{");

        sb.Append("--brand-primary:").Append(ToHex(primary)).Append(';');
        sb.Append("--brand-primary-aqua:").Append(ToHex(aqua)).Append(';');
        sb.Append("--brand-primary-hover:").Append(ToHex(hover)).Append(';');

        if (secondary is { } sec)
        {
            sb.Append("--brand-secondary:").Append(ToHex(sec)).Append(';');
        }

        if (accent is { } acc)
        {
            sb.Append("--brand-accent:").Append(ToHex(acc)).Append(';');
        }

        sb.Append("--text-on-primary:").Append(ToHex(onPrimary)).Append(';');

        // Derived interactive tints from the primary.
        sb.Append("--interactive-primary-soft:").Append(Rgba(primary, 0.12)).Append(';');
        sb.Append("--interactive-focus-ring:0 0 0 3px ").Append(Rgba(primary, 0.20)).Append(';');
        sb.Append("--border-strong:").Append(Rgba(primary, 0.34)).Append(';');

        // Chart slots that track the brand (1 primary, 6 aqua); others keep default.
        sb.Append("--chart-1:").Append(ToHex(primary)).Append(';');
        sb.Append("--chart-6:").Append(ToHex(aqua)).Append(';');

        sb.Append('}');
        return sb.ToString();
    }

    // ---- Colour model ------------------------------------------------------

    private readonly record struct Rgb(int R, int G, int B)
    {
        public static readonly Rgb White = new(255, 255, 255);
        public static readonly Rgb Black = new(0, 0, 0);
    }

    private readonly record struct Hsl(double H, double S, double L);

    private static bool TryParseHex(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6)
        {
            return false;
        }

        if (int.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            int.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            int.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            rgb = new Rgb(r, g, b);
            return true;
        }

        return false;
    }

    private static Rgb ParseKnownHex(string hex) =>
        TryParseHex(hex, out var rgb) ? rgb : Rgb.Black;

    private static string ToHex(Rgb c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Rgba(Rgb c, double alpha) =>
        $"rgba({c.R}, {c.G}, {c.B}, {alpha.ToString("0.##", CultureInfo.InvariantCulture)})";

    private static double ClampL(double l) => Math.Clamp(l, 0.0, 1.0);

    private static Hsl RgbToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2.0;
        double d = max - min;

        if (d == 0)
        {
            s = 0;
        }
        else
        {
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r)
            {
                h = (g - b) / d + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / d + 2;
            }
            else
            {
                h = (r - g) / d + 4;
            }

            h /= 6;
        }

        return new Hsl(h, s, l);
    }

    private static Rgb HslToRgb(Hsl hsl)
    {
        double r, g, b;

        if (hsl.S == 0)
        {
            r = g = b = hsl.L;
        }
        else
        {
            double q = hsl.L < 0.5 ? hsl.L * (1 + hsl.S) : hsl.L + hsl.S - hsl.L * hsl.S;
            double p = 2 * hsl.L - q;
            r = HueToRgb(p, q, hsl.H + 1.0 / 3.0);
            g = HueToRgb(p, q, hsl.H);
            b = HueToRgb(p, q, hsl.H - 1.0 / 3.0);
        }

        return new Rgb(
            (int)Math.Round(r * 255),
            (int)Math.Round(g * 255),
            (int)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    // WCAG relative luminance + contrast ratio.
    private static double RelativeLuminance(Rgb c)
    {
        static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double ContrastRatio(Rgb a, Rgb b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
