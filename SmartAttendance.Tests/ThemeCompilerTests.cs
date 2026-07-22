using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Unit tests for the Branding &amp; Theme Engine compiler. Pure and deterministic:
/// no database or web host required.
/// </summary>
public class ThemeCompilerTests
{
    private static ThemeCompiler.CompileResult Compile(string primary, string? secondary = null, string? accent = null) =>
        ThemeCompiler.Compile(new ThemeCompiler.BrandingInput(primary, secondary, accent));

    // ---- Happy path --------------------------------------------------------

    [Fact]
    public void ValidBrand_Passes_AndEmitsRootOverride()
    {
        var result = Compile("#18C7BD");

        Assert.Equal(ThemeCompiler.ValidationLevel.Pass, result.Level);
        Assert.StartsWith(":root{", result.CompiledCss);
        Assert.EndsWith("}", result.CompiledCss);
        Assert.Contains("--brand-primary:#18C7BD", result.CompiledCss);
        Assert.Equal("#18C7BD", result.NormalizedPrimaryHex);
    }

    [Fact]
    public void Compile_EmitsBrandDerivedTokens()
    {
        var css = Compile("#18C7BD").CompiledCss;

        Assert.Contains("--brand-primary:", css);
        Assert.Contains("--brand-primary-aqua:", css);
        Assert.Contains("--brand-primary-hover:", css);
        Assert.Contains("--text-on-primary:", css);
        Assert.Contains("--interactive-primary-soft:", css);
        Assert.Contains("--interactive-focus-ring:", css);
        Assert.Contains("--border-strong:", css);
        Assert.Contains("--chart-1:", css);
        Assert.Contains("--chart-6:", css);
    }

    [Fact]
    public void Compile_NeverOverridesSurfacesOrStatus()
    {
        // Semantic colours and surfaces stay on the ZYNORA Default by design.
        var css = Compile("#18C7BD", "#101C30", "#D4B36A").CompiledCss;

        Assert.DoesNotContain("--surface-", css);
        Assert.DoesNotContain("--status-", css);
        Assert.DoesNotContain("--text-default", css);
    }

    // ---- Optional secondary / accent --------------------------------------

    [Fact]
    public void SecondaryAndAccent_OnlyEmittedWhenProvided()
    {
        var without = Compile("#18C7BD").CompiledCss;
        Assert.DoesNotContain("--brand-secondary:", without);
        Assert.DoesNotContain("--brand-accent:", without);

        var with = Compile("#18C7BD", "#101C30", "#D4B36A").CompiledCss;
        Assert.Contains("--brand-secondary:#101C30", with);
        Assert.Contains("--brand-accent:#D4B36A", with);
    }

    // ---- on-primary contrast selection ------------------------------------

    [Fact]
    public void OnPrimary_IsDark_ForLightBrand()
    {
        // A very light brand needs dark text on top.
        var result = Compile("#EFEFEF");
        Assert.Equal("#000000", result.OnPrimaryHex);
    }

    [Fact]
    public void OnPrimary_IsLight_ForDarkButReadableBrand()
    {
        // A saturated blue is dark enough that white gives the best contrast.
        var result = Compile("#2F76C9");
        Assert.Equal("#FFFFFF", result.OnPrimaryHex);
    }

    // ---- Validation levels -------------------------------------------------

    [Theory]
    [InlineData("#000000")] // pure black — invisible on the dark shell
    [InlineData("#0A0A0A")] // near-black
    [InlineData("#081426")] // the app background itself
    public void UnreadableBrand_OnDarkShell_IsBlocked_WithEmptyCss(string hex)
    {
        var result = Compile(hex);

        Assert.Equal(ThemeCompiler.ValidationLevel.Block, result.Level);
        Assert.Equal(string.Empty, result.CompiledCss);
    }

    [Fact]
    public void BrightBrand_PassesContrast()
    {
        Assert.Equal(ThemeCompiler.ValidationLevel.Pass, Compile("#FFFFFF").Level);
        Assert.Equal(ThemeCompiler.ValidationLevel.Pass, Compile("#42DED3").Level);
    }

    // ---- Invalid / malformed input ----------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nothex")]
    [InlineData("#12")]
    [InlineData("#1234567")]
    public void InvalidPrimary_IsBlocked_WithEmptyCss(string? hex)
    {
        var result = ThemeCompiler.Compile(new ThemeCompiler.BrandingInput(hex!));

        Assert.Equal(ThemeCompiler.ValidationLevel.Block, result.Level);
        Assert.Equal(string.Empty, result.CompiledCss);
    }

    // ---- Parsing robustness ------------------------------------------------

    [Theory]
    [InlineData("#18C7BD")]
    [InlineData("18C7BD")]   // missing hash
    [InlineData("#18c7bd")]  // lowercase
    [InlineData(" #18C7BD ")] // whitespace
    public void PrimaryHex_IsNormalizedToUpperWithHash(string input)
    {
        var result = Compile(input);
        Assert.Equal("#18C7BD", result.NormalizedPrimaryHex);
    }

    [Fact]
    public void ShorthandHex_IsExpanded()
    {
        // #0AF -> #00AAFF
        var result = Compile("#0AF");
        Assert.Equal("#00AAFF", result.NormalizedPrimaryHex);
    }

    // ---- Determinism -------------------------------------------------------

    [Fact]
    public void Compile_IsDeterministic()
    {
        var a = Compile("#7C4DFF", "#101C30", "#D4B36A");
        var b = Compile("#7C4DFF", "#101C30", "#D4B36A");

        Assert.Equal(a.CompiledCss, b.CompiledCss);
        Assert.Equal(a.Level, b.Level);
        Assert.Equal(a.OnPrimaryHex, b.OnPrimaryHex);
    }

    [Fact]
    public void Compile_ProducesValidCssBraces()
    {
        var css = Compile("#7C4DFF").CompiledCss;
        Assert.Equal(1, css.Split('{').Length - 1);
        Assert.Equal(1, css.Split('}').Length - 1);
        Assert.DoesNotContain("*/", css); // no stray comment terminator (P3 lesson)
    }
}
