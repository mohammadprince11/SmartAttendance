using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس هوية الشركات (White-Label): أياً كان اللون الذي تختاره شركة، النص والأزرار
/// تبقى مقروءة. الحالة الواقعية التي كشفت الثغرة: <c>#18C7BD</c> المحقون بمحرّك
/// الهوية أعطى نصاً بنسبة 2.11:1 على الأبيض.
/// </summary>
public class AccessibleBrandPaletteTests
{
    private const string White = "#ffffff";
    private const string Graphite = "#1b222a";
    private const string BrandTeal = "#18c7bd";   // اللون الحيّ فعلاً بالنظام

    private static double Ratio(string a, string b) => ColorContrast.Ratio(a, b);

    // ===== الحالة التي كشفت الثغرة =====

    [Fact]
    public void LiveBrandTeal_IsUnreadableAsRawTextOnWhite() =>
        Assert.True(Ratio(BrandTeal, White) < ColorContrast.NormalTextThreshold,
            "لو صار هذا اللون مقروءاً خاماً فقد الاختبار معناه");

    [Fact]
    public void DerivedTextVariant_BecomesReadableOnWhite() =>
        Assert.True(Ratio(AccessibleBrandPalette.EnsureReadableOn(BrandTeal, White), White)
                    >= ColorContrast.NormalTextThreshold);

    [Fact]
    public void DerivedButton_CarriesWhiteInk() =>
        Assert.True(Ratio(White, AccessibleBrandPalette.EnsureCarriesInk(BrandTeal, White))
                    >= ColorContrast.NormalTextThreshold);

    /// <summary>على الغرافيت اللون مقروء أصلاً ⟹ يُترك كما هو بلا عبث.</summary>
    [Fact]
    public void ReadableColor_IsReturnedUnchanged() =>
        Assert.Equal(BrandTeal, AccessibleBrandPalette.EnsureReadableOn(BrandTeal, Graphite));

    // ===== ألوان علامات متنوّعة: القاعدة تصمد لأيّ اختيار =====

    [Theory]
    [InlineData("#ffe600")]  // أصفر صارخ — أسوأ حالة على الأبيض
    [InlineData("#00ff00")]  // أخضر نيون
    [InlineData("#18c7bd")]  // التيل الحيّ
    [InlineData("#f97316")]  // برتقالي
    [InlineData("#0ea5b5")]  // تيل ZYNORA الأصلي
    [InlineData("#111827")]  // لون داكن جداً (لا يحتاج تعديلاً)
    public void AnyBrandColor_YieldsReadableTextOnWhite(string brand)
    {
        var derived = AccessibleBrandPalette.EnsureReadableOn(brand, White);

        Assert.True(Ratio(derived, White) >= ColorContrast.NormalTextThreshold,
            $"{brand} ⟵ {derived} = {Ratio(derived, White):F2}:1");
    }

    [Theory]
    [InlineData("#ffe600")]
    [InlineData("#00ff00")]
    [InlineData("#18c7bd")]
    [InlineData("#f97316")]
    public void AnyBrandColor_YieldsButtonCarryingWhiteInk(string brand)
    {
        var background = AccessibleBrandPalette.EnsureCarriesInk(brand, White);

        Assert.True(Ratio(White, background) >= ColorContrast.NormalTextThreshold,
            $"{brand} ⟵ {background} = {Ratio(White, background):F2}:1");
    }

    [Theory]
    [InlineData("#18c7bd")]
    [InlineData("#0ea5b5")]
    [InlineData("#111827")]  // داكن على غرافيت — يحتاج تفتيحاً لا تعتيماً
    [InlineData("#1b222a")]  // نفس لون السطح تماماً
    public void AnyBrandColor_YieldsReadableTextOnGraphite(string brand)
    {
        var derived = AccessibleBrandPalette.EnsureReadableOn(brand, Graphite);

        Assert.True(Ratio(derived, Graphite) >= ColorContrast.NormalTextThreshold,
            $"{brand} ⟵ {derived} = {Ratio(derived, Graphite):F2}:1");
    }

    // ===== اختيار الحبر =====

    [Fact]
    public void LightBackground_PicksDarkInk() =>
        Assert.Equal("#08303a", AccessibleBrandPalette.PickInk("#35aebb"));

    [Fact]
    public void DarkBackground_PicksWhiteInk() =>
        Assert.Equal("#ffffff", AccessibleBrandPalette.PickInk("#0a2540"));

    // ===== المزج =====

    [Fact]
    public void Mix_AtZero_KeepsTheSourceColor() =>
        Assert.Equal("#18c7bd", AccessibleBrandPalette.Mix(BrandTeal, "#000000", 0));

    [Fact]
    public void Mix_AtOne_ReachesTheTarget() =>
        Assert.Equal("#000000", AccessibleBrandPalette.Mix(BrandTeal, "#000000", 1));

    /// <summary>
    /// المنتصف بالضبط: 0x18→0x0c · 0xc7→0x64 (99.5 ⟵ تقريب مصرفي) · 0xbd→0x5e (94.5).
    /// </summary>
    [Fact]
    public void Mix_Midpoint_IsHalfway() =>
        Assert.Equal("#0c645e", AccessibleBrandPalette.Mix(BrandTeal, "#000000", 0.5));

    /// <summary>التعديل يحافظ على الصبغة: الأخضر يبقى أخضر لا يصير رمادياً.</summary>
    [Fact]
    public void DerivedColor_KeepsTheBrandHueRecognizable()
    {
        var derived = AccessibleBrandPalette.EnsureReadableOn("#00ff00", White);
        var (r, g, b) = (
            Convert.ToInt32(derived.Substring(1, 2), 16),
            Convert.ToInt32(derived.Substring(3, 2), 16),
            Convert.ToInt32(derived.Substring(5, 2), 16));

        Assert.True(g > r && g > b, $"القناة الخضراء يجب أن تبقى الغالبة — {derived}");
    }

    [Fact]
    public void InvalidHex_FailsLoudly() =>
        Assert.Throws<ArgumentException>(() => AccessibleBrandPalette.EnsureReadableOn("cyan", White));
}
