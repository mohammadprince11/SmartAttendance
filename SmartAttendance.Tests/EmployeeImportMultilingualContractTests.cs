using SmartAttendance.Web.Infrastructure.Imports;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeImportMultilingualContractTests
{
    [Theory]
    [InlineData("الاسم الأول [ar-IQ] *", "FirstName", "ar-IQ")]
    [InlineData("اللقب [en-US]", "LastName", "en-US")]
    [InlineData("اسم الشركة [ckb-IQ]", "CompanyName", "ckb-IQ")]
    [InlineData("اسم موقع العمل [fr-FR]", "WorkLocationName", "fr-FR")]
    [InlineData("اسم القسم [tr-TR]", "DepartmentName", "tr-TR")]
    [InlineData("المسمى الوظيفي [de-DE]", "PositionName", "de-DE")]
    public void Localized_template_header_preserves_field_and_culture(
        string header,
        string expectedField,
        string expectedCulture)
    {
        var parsed = EmployeeBootstrapImportEngine.TryParseLocalizedHeader(
            header,
            out var field,
            out var culture);

        Assert.True(parsed);
        Assert.Equal(expectedField, field);
        Assert.Equal(expectedCulture, culture);
    }

    [Theory]
    [InlineData("الاسم الأول")]
    [InlineData("اسم القسم [???]")]
    [InlineData("الراتب الأساسي [ar-IQ]")]
    public void Non_localized_or_non_translatable_headers_are_not_misclassified(
        string header)
    {
        Assert.False(EmployeeBootstrapImportEngine.TryParseLocalizedHeader(
            header,
            out _,
            out _));
    }

    [Fact]
    public void Localized_value_without_company_default_has_no_arabic_bias()
    {
        var method = typeof(EmployeeBootstrapImportEngine).GetMethod(
            "GetPreferredLocalizedValue",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var values = new Dictionary<string, string>
        {
            ["FirstName [fr-FR]"] = "Jean",
            ["FirstName [ar-IQ]"] = "Mohammed"

        };

        var result = method!.Invoke(
            null,
            new object?[] { values, "FirstName", null });

        Assert.Equal("Jean", Assert.IsType<string>(result));
    }
}
