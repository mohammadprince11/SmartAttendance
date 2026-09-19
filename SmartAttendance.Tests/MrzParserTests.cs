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
