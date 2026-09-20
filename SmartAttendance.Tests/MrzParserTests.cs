using SmartAttendance.Application.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class MrzParserTests
{
    [Fact]
    public void Td3_ParsesAndValidatesIcaoSample()
    {
        const string mrz =
            "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<\n" +
            "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

        var result = MrzParser.Parse(mrz);

        Assert.NotNull(result);
        Assert.Equal("TD3", result!.Format);
        Assert.Equal("L898902C3", result.DocumentNumber);
        Assert.Equal("UTO", result.Nationality);
        Assert.Equal(new DateOnly(1974, 8, 12), result.DateOfBirth);
        Assert.Equal(new DateOnly(2012, 4, 15), result.ExpiryDate);
        Assert.Equal("ERIKSSON", result.Surname);
        Assert.Equal(["ANNA", "MARIA"], result.GivenNames);
        Assert.True(result.AllRequiredChecksValid);
    }

    [Fact]
    public void Td1_ParsesAndValidatesIcaoSample()
    {
        const string mrz =
            "I<UTOD231458907<<<<<<<<<<<<<<<\n" +
            "7408122F1204159UTO<<<<<<<<<<<6\n" +
            "ERIKSSON<<ANNA<MARIA<<<<<<<<<<";

        var result = MrzParser.Parse(mrz);

        Assert.NotNull(result);
        Assert.Equal("TD1", result!.Format);
        Assert.Equal("D23145890", result.DocumentNumber);
        Assert.Equal("UTO", result.Nationality);
        Assert.Equal(new DateOnly(1974, 8, 12), result.DateOfBirth);
        Assert.Equal(new DateOnly(2012, 4, 15), result.ExpiryDate);
        Assert.Equal("ERIKSSON", result.Surname);
        Assert.True(result.AllRequiredChecksValid);
    }

    [Fact]
    public void OcrFragments_ReassembleTd3PassportMrz()
    {
        const string line1 =
            "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
        const string line2 =
            "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

        var result = MrzOcrParser.Parse(
        [
            line1[..19],
            line1[19..],
            line2[..21],
            line2[21..]
        ]);

        Assert.NotNull(result);
        Assert.Equal("TD3", result!.Format);
        Assert.Equal("L898902C3", result.DocumentNumber);
        Assert.Equal("F", result.Sex);
        Assert.True(result.AllRequiredChecksValid);
    }

    [Fact]
    public void OcrFragments_ReassembleTd1NationalIdMrz()
    {
        const string line1 = "I<UTOD231458907<<<<<<<<<<<<<<<";
        const string line2 = "7408122F1204159UTO<<<<<<<<<<<6";
        const string line3 = "ERIKSSON<<ANNA<MARIA<<<<<<<<<<";

        var result = MrzOcrParser.Parse(
        [
            line1[..14], line1[14..],
            line2[..16], line2[16..],
            line3[..12], line3[12..]
        ]);

        Assert.NotNull(result);
        Assert.Equal("TD1", result!.Format);
        Assert.Equal("D23145890", result.DocumentNumber);
        Assert.Equal("F", result.Sex);
        Assert.True(result.AllRequiredChecksValid);
    }

    [Theory]
    [InlineData("L898902C3", '6')]
    [InlineData("740812", '2')]
    [InlineData("120415", '9')]
    public void CheckDigit_ValidatesKnownValues(
        string value,
        char expected)
    {
        Assert.True(MrzParser.ValidateCheckDigit(value, expected));
    }
}
