using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// قاعدة اشتقاق المواطنة من الجنسية — المنطق النقي (Parse/IsCitizen).
/// الحالات مأخوذة من سلوك كيان الموثّق بدراسة 2026-08-15: «مواطن إذا سعودي»،
/// والتطبيع يتسامح مع «ال» التعريف والمسافات والفاصلة العربية.
/// </summary>
public class CitizenshipPolicyTests
{
    [Fact]
    public void Parse_SplitsOnArabicAndLatinCommas()
    {
        var result = CitizenshipPolicy.Parse("سعودي، أردني, كويتي; إماراتي");
        Assert.Equal(new[] { "سعودي", "أردني", "كويتي", "إماراتي" }, result);
    }

    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(CitizenshipPolicy.Parse(null));
        Assert.Empty(CitizenshipPolicy.Parse("   "));
        Assert.Empty(CitizenshipPolicy.Parse("،،،"));
    }

    [Fact]
    public void Parse_DeduplicatesAfterNormalization()
    {
        // «السعودي» و«سعودي » ينطبعان لنفس القيمة — لا صفوف مكررة.
        var result = CitizenshipPolicy.Parse("سعودي، السعودي، سعودي ");
        Assert.Single(result);
    }

    [Theory]
    [InlineData("سعودي", true)]
    [InlineData("السعودي", true)]   // «ال» التعريف لا تفرّق
    [InlineData(" سعودي ", true)]   // المسافات لا تفرّق
    [InlineData("أردني", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCitizen_DefaultSaudiRule(string? nationality, bool expected)
    {
        Assert.Equal(expected, CitizenshipPolicy.IsCitizen(nationality, CitizenshipPolicy.DefaultNationalities));
    }

    [Fact]
    public void IsCitizen_MultiNationalityList()
    {
        const string configured = "سعودي، أردني";
        Assert.True(CitizenshipPolicy.IsCitizen("أردني", configured));
        Assert.True(CitizenshipPolicy.IsCitizen("الأردني", configured));
        Assert.False(CitizenshipPolicy.IsCitizen("مصري", configured));
    }

    [Fact]
    public void IsCitizen_EmptyConfiguredList_NeverCitizen()
    {
        Assert.False(CitizenshipPolicy.IsCitizen("سعودي", ""));
        Assert.False(CitizenshipPolicy.IsCitizen("سعودي", null));
    }
}
