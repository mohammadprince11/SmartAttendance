using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس راحة العين: يثبّت أن كل زوج (نص/خلفية) بهوية ZYNORA يحقّق حدود WCAG.
///
/// القيم هنا **نسخة من** <c>wwwroot/css/zynora-design-system.css</c>. تغييرها
/// بالـCSS دون تحديثها هنا لا يُكشف — لكن تغييرها هنا لقيمة راسبة يُفشل الاختبار،
/// وهو ما يمنع «تفتيح» لون تدريجياً حتى يعود الإجهاد. أي تعديل لوني يمرّ من هنا.
/// </summary>
public class PaletteContrastTests
{
    // ===== الفاتح =====
    private const string LightSurface = "#ffffff";
    private const string LightBackground = "#f2f5f9";
    private const string LightTextPrimary = "#16283a";
    private const string LightTextSecondary = "#55687b";
    private const string LightPrimaryStrong = "#0a7286";
    private const string LightPrimaryText = "#0a7286";
    private const string LightOnPrimary = "#ffffff";
    private const string LightBorderStrong = "#74899f";
    private const string LightSuccess = "#15803d";
    private const string LightSuccessSoft = "#ecfdf3";
    private const string LightWarning = "#b45309";
    private const string LightWarningSoft = "#fff7e6";
    private const string LightDanger = "#b91c1c";
    private const string LightDangerSoft = "#fdecec";

    // ===== الداكن =====
    private const string DarkSurface = "#1b222a";
    private const string DarkBackground = "#14191f";
    private const string DarkTextPrimary = "#c9d4dd";
    private const string DarkTextSecondary = "#8496a5";
    private const string DarkPrimary = "#35aebb";
    private const string DarkOnPrimary = "#08303a";
    private const string DarkBorderStrong = "#697a8b";
    private const string DarkSuccess = "#66b789";
    private const string DarkSuccessSoft = "#1c2921";
    private const string DarkWarning = "#cfa14c";
    private const string DarkWarningSoft = "#2a2419";
    private const string DarkDanger = "#cd7676";
    private const string DarkDangerSoft = "#2a1e1e";

    private static void AssertReadable(string foreground, string background)
    {
        var ratio = ColorContrast.Ratio(foreground, background);

        Assert.True(
            ratio >= ColorContrast.NormalTextThreshold,
            $"{foreground} على {background} = {ratio:F2}:1 — دون حد النص العادي (4.5:1)");
    }

    private static void AssertVisibleBorder(string foreground, string background)
    {
        var ratio = ColorContrast.Ratio(foreground, background);

        Assert.True(
            ratio >= ColorContrast.LargeTextThreshold,
            $"{foreground} على {background} = {ratio:F2}:1 — دون حد حدود العناصر (3:1)");
    }

    // ===== النصوص بالوضع الفاتح =====

    [Theory]
    [InlineData(LightTextPrimary, LightSurface)]
    [InlineData(LightTextPrimary, LightBackground)]
    [InlineData(LightTextSecondary, LightSurface)]
    [InlineData(LightTextSecondary, LightBackground)]
    [InlineData(LightPrimaryText, LightSurface)]
    public void LightText_IsReadable(string fg, string bg) => AssertReadable(fg, bg);

    /// <summary>الزر الموحَّد بكل النظام — كان أضعف نقطة (2.97:1).</summary>
    [Fact]
    public void LightPrimaryButton_IsReadable() =>
        AssertReadable(LightOnPrimary, LightPrimaryStrong);

    [Theory]
    [InlineData(LightSuccess, LightSuccessSoft)]
    [InlineData(LightWarning, LightWarningSoft)]
    [InlineData(LightDanger, LightDangerSoft)]
    public void LightStatusChips_AreReadable(string fg, string bg) => AssertReadable(fg, bg);

    [Fact]
    public void LightInteractiveBorder_IsVisible() =>
        AssertVisibleBorder(LightBorderStrong, LightSurface);

    // ===== النصوص بالوضع الداكن =====

    [Theory]
    [InlineData(DarkTextPrimary, DarkSurface)]
    [InlineData(DarkTextPrimary, DarkBackground)]
    [InlineData(DarkTextSecondary, DarkSurface)]
    [InlineData(DarkTextSecondary, DarkBackground)]
    [InlineData(DarkPrimary, DarkSurface)]
    public void DarkText_IsReadable(string fg, string bg) => AssertReadable(fg, bg);

    /// <summary>بالداكن النص فوق الزر داكن لا أبيض — أبيض عليه 2.65:1 فقط.</summary>
    [Fact]
    public void DarkPrimaryButton_UsesDarkInkAndIsReadable()
    {
        AssertReadable(DarkOnPrimary, DarkPrimary);
        Assert.True(ColorContrast.Ratio("#ffffff", DarkPrimary) < ColorContrast.NormalTextThreshold,
            "أبيض على تيل الداكن يجب أن يبقى مرفوضاً — وإلا فقدنا سبب اختيار الحبر الداكن");
    }

    [Theory]
    [InlineData(DarkSuccess, DarkSuccessSoft)]
    [InlineData(DarkWarning, DarkWarningSoft)]
    [InlineData(DarkDanger, DarkDangerSoft)]
    public void DarkStatusChips_AreReadable(string fg, string bg) => AssertReadable(fg, bg);

    [Fact]
    public void DarkInteractiveBorder_IsVisible() =>
        AssertVisibleBorder(DarkBorderStrong, DarkSurface);

    // ===== الحد الأعلى: تباين مفرط يُتعب أيضاً =====

    /// <summary>
    /// الأسود النقي على الأبيض النقي (21:1) يسبب وهجاً بالجلسات الطويلة. نبقي
    /// النص الأساسي دون 16:1 — كحلي داكن لا أسود.
    /// </summary>
    [Fact]
    public void LightPrimaryText_AvoidsHarshMaximumContrast()
    {
        var ratio = ColorContrast.Ratio(LightTextPrimary, LightSurface);

        Assert.InRange(ratio, 7.0, 16.0);
    }

    [Fact]
    public void DarkPrimaryText_AvoidsHarshMaximumContrast()
    {
        // الأبيض النقي على الغرافيت يُحدث «هالة» بالإضاءة المنخفضة.
        Assert.InRange(ColorContrast.Ratio(DarkTextPrimary, DarkSurface), 7.0, 13.0);
    }

    // ===== الحاسبة نفسها =====

    [Fact]
    public void Ratio_IsSymmetric() =>
        Assert.Equal(
            ColorContrast.Ratio("#16283a", "#ffffff"),
            ColorContrast.Ratio("#ffffff", "#16283a"),
            precision: 6);

    [Fact]
    public void BlackOnWhite_IsTheKnownMaximum() =>
        Assert.Equal(21.0, ColorContrast.Ratio("#000000", "#ffffff"), precision: 1);

    [Fact]
    public void SameColor_HasNoContrast() =>
        Assert.Equal(1.0, ColorContrast.Ratio("#0a7286", "#0a7286"), precision: 6);

    [Fact]
    public void ShorthandHex_IsSupported() =>
        Assert.Equal(
            ColorContrast.Ratio("#fff", "#000"),
            ColorContrast.Ratio("#ffffff", "#000000"),
            precision: 6);

    [Fact]
    public void InvalidHex_FailsLoudly() =>
        Assert.Throws<ArgumentException>(() => ColorContrast.Ratio("teal", "#ffffff"));
}
