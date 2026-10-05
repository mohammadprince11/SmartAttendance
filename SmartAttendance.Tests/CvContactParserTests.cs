using SmartAttendance.Application.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class CvContactParserTests
{
    [Fact]
    public void Parse_ExtractsPhoneAndEmailFromCvContactBlock()
    {
        var result = CvContactParser.Parse(
        [
            "MOHAMMAD ALI ZIDANE",
            "Contact",
            "07736826839",
            "ali_momad2000@yahoo.com",
            "Baghdad",
            "1992-03-25",
            "Iraq"
        ]);

        Assert.Equal("07736826839", result.Phone);
        Assert.Equal(
            "ali_momad2000@yahoo.com",
            result.Email);
    }

    [Fact]
    public void Parse_DoesNotTreatDateAsPhone()
    {
        var result = CvContactParser.Parse(
        [
            "1992-03-25",
            "Baghdad",
            "Iraq"
        ]);

        Assert.Null(result.Phone);
    }

    [Fact]
    public void Parse_NormalizesFormattedInternationalPhone()
    {
        var result = CvContactParser.Parse(
        [
            "+964 773 682 6839"
        ]);

        Assert.Equal("+9647736826839", result.Phone);
    }
}
