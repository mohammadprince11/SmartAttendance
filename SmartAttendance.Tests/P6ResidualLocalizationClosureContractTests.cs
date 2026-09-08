using System.Xml.Linq;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class P6ResidualLocalizationClosureContractTests
{
    [Fact]
    public void AttendancePeriodNote_UsesServerSideDynamicLocalization()
    {
        var source = ReadWeb("Pages", "AttendanceRecords", "Index.cshtml");

        Assert.Contains(
            "T[\"افتراضياً ({0}) — حدّد تواريخك للتوسيع، أو\", Model.DefaultPeriodNote]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PayrollBracketLabel_UsesServerSideLocalization()
    {
        var source = ReadWeb("Pages", "Payroll", "Settings.cshtml");

        Assert.Contains(
            "T[\"· شرائح:\"]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PeopleConfiguration_UsesLocalizedHint_AndInheritsDirection()
    {
        var source = ReadWeb("Pages", "HrSettings", "PeopleConfiguration.cshtml");

        Assert.Contains(
            "T[\"— المطابقة تتسامح مع «ال» التعريف والمسافات الزائدة.\"]",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "<section class=\"nxhs-page\" dir=\"rtl\">",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UserAccess_UsesLocalizedDynamicAlert_AndInheritsDirection()
    {
        var source = ReadWeb("Pages", "UserAccess", "Index.cshtml");

        Assert.Contains(
            "T[\"يوجد {0} موظّف بلا حساب دخول — يُعرض منهم {1} فقط لأداء أفضل.",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "<section class=\"zua-page\" dir=\"rtl\">",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Dictionary_VisibilityStatus_FollowsUiCulture()
    {
        var source = ReadWeb("Pages", "Settings", "Dictionary.cshtml");

        Assert.Contains(
            "language.IsHidden ? T[\"مخفية\"].Value : T[\"ظاهرة\"].Value",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "افتراضياً ({0}) — حدّد تواريخك للتوسيع، أو",
        "Default ({0}) — choose dates to expand the range, or")]
    [InlineData("· شرائح:", "· Brackets:")]
    [InlineData("ظاهرة", "Visible")]
    [InlineData("مخفية", "Hidden")]
    [InlineData(
        "— المطابقة تتسامح مع «ال» التعريف والمسافات الزائدة.",
        "— Matching ignores the Arabic definite article and extra spaces.")]
    public void ResidualKeys_AreCleanInEnglishCatalog(
        string key,
        string expected)
    {
        var catalog = LoadCatalog("SharedResource.en-US.resx");

        Assert.True(catalog.TryGetValue(key, out var value));
        Assert.Equal(expected, value);
        Assert.DoesNotMatch(@"[\u0600-\u06FF]", value);
    }

    [Theory]
    [InlineData("افتراضياً ({0}) — حدّد تواريخك للتوسيع، أو")]
    [InlineData("· شرائح:")]
    [InlineData("ظاهرة")]
    [InlineData("مخفية")]
    [InlineData("— المطابقة تتسامح مع «ال» التعريف والمسافات الزائدة.")]
    public void ResidualKeys_KeepKurdishCatalogParity(string key)
    {
        var catalog = LoadCatalog("SharedResource.ckb-IQ.resx");

        Assert.True(catalog.TryGetValue(key, out var value));
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    private static Dictionary<string, string> LoadCatalog(string fileName)
    {
        var path = Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Resources",
            fileName);

        var document = XDocument.Load(path);

        return document.Root!
            .Elements("data")
            .Where(item => item.Attribute("name") is not null)
            .ToDictionary(
                item => item.Attribute("name")!.Value,
                item => item.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(
            Path.Combine(
                new[] { FindRoot(), "SmartAttendance.Web" }
                    .Concat(parts)
                    .ToArray()));

    private static string FindRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}