using SmartAttendance.Application.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PassportVisualParserTests
{
    [Fact]
    public void IraqiPassport_VisualLabelsRecoverFieldsWhenMrzIsMissing()
    {
        var result = PassportVisualParser.Parse(
        [
            L("Country", 1219, 394, 1614, 458),
            L("IRQ", 1364, 435, 1484, 494),
            L("A17346829", 1811, 460, 2107, 520),
            L("Full Name", 746, 513, 906, 558),
            L("MOHAMMED ALI ZAIDAN", 737, 554, 1448, 644),
            L("Surname", 733, 621, 883, 669),
            L("AL-SUDANI", 733, 657, 1064, 727),
            L("Nationality/", 1331, 642, 1724, 698),
            L("IRAQI/", 1386, 677, 1693, 759),
            L("Sex", 724, 720, 795, 764),
            L("MIS", 727, 797, 907, 857),
            L("Date of Birth", 1294, 744, 1496, 789),
            L("1992-03-25", 1229, 814, 1538, 886),
            L("Mother Name", 712, 918, 928, 964),
            L("SUHAD CHASIB", 716, 968, 1176, 1044),
            L("Date of Expiry", 711, 1034, 939, 1080),
            L("Date of Issue", 1269, 1055, 1482, 1105),
            L("Issuing Authority", 1950, 1083, 2226, 1135),
            L("2027-12-23", 704, 1111, 1017, 1179),
            L("2019-12-24", 1210, 1130, 1523, 1200),
            L("BAGHDAD/L", 1800, 1156, 2214, 1229)
        ]);

        Assert.Equal("A17346829", result.DocumentNumber);
        Assert.Equal("MOHAMMED ALI ZAIDAN", result.GivenNames);
        Assert.Equal("AL-SUDANI", result.Surname);
        Assert.Equal("IRAQI", result.Nationality);
        Assert.Equal("M", result.Sex);
        Assert.Equal("1992-03-25", result.DateOfBirth);
        Assert.Equal("2027-12-23", result.ExpiryDate);
        Assert.Equal("2019-12-24", result.IssueDate);
        Assert.Equal("IRQ", result.IssuingCountry);
        Assert.Equal("SUHAD CHASIB", result.MotherName);
        Assert.True(result.HasStrongIdentityEvidence);
    }

    [Fact]
    public void VisualNames_CollapseRepeatedWhitespace()
    {
        var result = PassportVisualParser.Parse(
        [
            L("Full Name", 100, 100, 300, 150),
            L("MOHAMMED   ALI   ZAIDAN", 100, 155, 500, 220)
        ]);

        Assert.Equal("MOHAMMED ALI ZAIDAN", result.GivenNames);
    }

    private static PassportVisualOcrLine L(
        string text, int x1, int y1, int x2, int y2) =>
        new(text, .95, [x1, y1, x2, y2]);
}
