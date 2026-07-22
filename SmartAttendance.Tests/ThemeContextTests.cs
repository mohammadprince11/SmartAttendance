using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>Pure tests for the theme context model and cache key contract.</summary>
public class ThemeContextTests
{
    [Fact]
    public void Default_HasNoCompanyOverride()
    {
        Assert.False(ThemeContext.Default.HasCompanyOverride);
        Assert.Null(ThemeContext.Default.CompanyId);
        Assert.Equal(ThemeContext.DefaultVersion, ThemeContext.Default.Version);
        Assert.Equal(string.Empty, ThemeContext.Default.CompiledCss);
    }

    [Fact]
    public void HasCompanyOverride_TrueOnlyWithCompiledCss()
    {
        Assert.False(new ThemeContext { CompanyId = 5 }.HasCompanyOverride);
        Assert.False(new ThemeContext { CompanyId = 5, CompiledCss = "   " }.HasCompanyOverride);
        Assert.True(new ThemeContext { CompanyId = 5, CompiledCss = ":root{--brand-primary:#000}" }.HasCompanyOverride);
    }

    [Theory]
    [InlineData(1, "CompanyTheme:1")]
    [InlineData(42, "CompanyTheme:42")]
    public void CompanyCacheKey_IsStableAndCompanyScoped(int companyId, string expected)
    {
        Assert.Equal(expected, ThemeContextService.CompanyCacheKey(companyId));
    }

    [Fact]
    public void CompanyCacheKey_IsDistinctPerCompany()
    {
        Assert.NotEqual(
            ThemeContextService.CompanyCacheKey(1),
            ThemeContextService.CompanyCacheKey(2));
    }
}
