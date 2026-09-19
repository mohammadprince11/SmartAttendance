using SmartAttendance.Web.Infrastructure.Ui;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeLookupNormalizationTests
{
    [Theory]
    [InlineData("IRQ", "Iraq", "Iraqi")]
    [InlineData("JOR", "Jordan", "Jordanian")]
    [InlineData("SYR", "Syria", "Syrian")]
    [InlineData("ARE", "United Arab Emirates", "Emirati")]
    [InlineData("PHL", "Philippines", "Filipino")]
    public void Iso3Code_MapsToEmployeeDropdownValues(
        string code,
        string expectedCountry,
        string expectedNationality)
    {
        Assert.Equal(
            expectedCountry,
            ZynoraEmployeeLookups.NormalizePrimaryCountry(code));
        Assert.Equal(
            expectedNationality,
            ZynoraEmployeeLookups.NormalizePrimaryNationality(code));
    }

    [Fact]
    public void ExistingDropdownValues_ArePreserved()
    {
        Assert.Equal(
            "Iraq",
            ZynoraEmployeeLookups.NormalizePrimaryCountry("Iraq"));
        Assert.Equal(
            "Iraqi",
            ZynoraEmployeeLookups.NormalizePrimaryNationality("Iraqi"));
    }

    [Fact]
    public void UnsupportedCountryCode_FallsBackToOtherDropdownValue()
    {
        Assert.Equal(
            "Other",
            ZynoraEmployeeLookups.NormalizePrimaryCountry("USA"));
        Assert.Equal(
            "Other",
            ZynoraEmployeeLookups.NormalizePrimaryNationality("USA"));
    }
}
