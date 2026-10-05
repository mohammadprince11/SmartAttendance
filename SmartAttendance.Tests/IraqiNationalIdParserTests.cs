using SmartAttendance.Application.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class IraqiNationalIdParserTests
{
    [Fact]
    public void FrontSide_ExtractsIraqiNationalIdCoreIdentityFields()
    {
        var lines = new[]
        {
            L("البطاقة الوطنية اكارني نيشتمانى", 421, 685, 761, 747),
            L("199276728473", 429, 724, 767, 787),
            L("محمد", 796, 816, 924, 861),
            L("الاسم لاناو", 947, 806, 1120, 865),
            L("علي", 812, 861, 934, 922),
            L("الاب باوك", 910, 856, 1122, 919),
            L("الجدبابير زيدان", 794, 911, 1122, 972),
            L("اللقبنارناو  السوداني", 742, 962, 1121, 1028),
            L("الأم دايك سهاد", 805, 1014, 1123, 1076),
            L("الجد بابير", 941, 1067, 1121, 1122),
            L("الجنس اردكه", 943, 1117, 1117, 1171),
            L("AR4543924", 67, 1190, 396, 1256)
        };
        var result = IraqiNationalIdParser.Parse(lines);

        Assert.Equal("199276728473", result.NationalNumber);
        Assert.Equal("AR4543924", result.DocumentNumber);
        Assert.Equal("محمد", result.FirstName);
        Assert.Equal("علي", result.SecondName);
        Assert.Equal("زيدان", result.ThirdName);
        Assert.Equal("السوداني", result.LastName);
        Assert.Equal("سهاد", result.MotherName);
    }

    [Fact]
    public void FrontSide_GluedKurdishFirstNameLabel_IsRemoved()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("الاسم ناوسالم", 797, 809, 1117, 865)
        ]);

        Assert.Equal("سالم", result.FirstName);
    }

    [Fact]
    public void FrontSide_LegitimateNameStartingWithNaoLetters_IsNotTrimmed()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("الاسم نواف", 797, 809, 1117, 865)
        ]);

        Assert.Equal("نواف", result.FirstName);
    }

    [Fact]
    public void FrontSide_NeighborKurdishLabels_AreRemovedFromNames()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("الأب", 960, 850, 1120, 920),
            L("رباوك علي", 760, 850, 940, 920),
            L("الجد", 960, 920, 1120, 990),
            L("بابير زيدان", 740, 920, 940, 990),
            L("اللقب", 960, 990, 1120, 1060),
            L("نازناو السوداني", 700, 990, 940, 1060),
            L("الأم", 960, 1060, 1120, 1130),
            L("ادايك سهاد", 760, 1060, 940, 1130)
        ]);

        Assert.Equal("علي", result.SecondName);
        Assert.Equal("زيدان", result.ThirdName);
        Assert.Equal("السوداني", result.LastName);
        Assert.Equal("سهاد", result.MotherName);
    }

    [Theory]
    [InlineData("الجنس ذكر", "M")]
    [InlineData("الجنس أنثى", "F")]
    public void FrontSide_Sex_IsExtractedWhenClearlyReadable(
        string line,
        string expected)
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L(line, 900, 1100, 1200, 1170)
        ]);

        Assert.Equal(expected, result.Sex);
    }

    [Fact]
    public void ArabicIndicDigits_AreNormalizedForIdentityNumbers()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            new("١٩٩٢٧٦٧٢٨٤٧٣", .99, null),
            new("AR4543924", .99, null)
        ]);

        Assert.Equal("199276728473", result.NationalNumber);
        Assert.Equal("AR4543924", result.DocumentNumber);
    }

    [Fact]
    public void BackSide_FamilyNumber_IsReadFromLabelNeighbor()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("الرقم العانليژماردى خيزاني", 700, 417, 1527, 493),
            L("12345678", 350, 420, 650, 490)
        ]);

        Assert.Equal("12345678", result.FamilyNumber);
    }

    [Fact]
    public void BackSide_FamilyNumber_PreservesAlphaNumericPrefix()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("الرقم العانلي ژماردى خيزاني", 980, 410, 1530, 500),
            L("1010E1876147874699", 60, 410, 940, 500)
        ]);

        Assert.Equal("1010E1876147874699", result.FamilyNumber);
    }

    [Fact]
    public void BackSide_UnrelatedArabicLabels_DoNotBecomeNames()
    {
        var result = IraqiNationalIdParser.Parse(
        [
            L("المدنية", 132, 54, 290, 132),
            L("الجنسية", 555, 53, 734, 127),
            L("جهة الاصدار الابعنى دهرجون مديرية", 737, 48, 1533, 146),
            L("والمعلومات", 291, 58, 549, 128),
            L("تأريخ الاصدار ا روزى دهرجوون", 568, 130, 1531, 213),
            L("IDIRQAR45439248199276728473<<<", 60, 688, 1526, 754)
        ]);

        Assert.Null(result.FirstName);
        Assert.Null(result.SecondName);
        Assert.Null(result.ThirdName);
        Assert.Null(result.LastName);
        Assert.Null(result.MotherName);
    }

    private static IraqiNationalIdOcrLine L(
        string text,
        int x1,
        int y1,
        int x2,
        int y2) =>
        new(text, .95, [x1, y1, x2, y2]);
}
