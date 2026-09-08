using System.Xml.Linq;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class P6LocalizationFinalCleanupContractTests
{
    [Fact]
    public void SelectSystem_DoesNotUseWholeContainingLabelWithOptions()
    {
        var source = ReadWeb("wwwroot", "js", "zynora-select-system.js");

        Assert.Contains("function cleanAccessibleText", source, StringComparison.Ordinal);
        Assert.Contains("function humanizeTechnicalName", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "containingLabel && containingLabel.textContent",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"select, option, input, textarea, button, script, style\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UxGuards_AvoidBroadContainerTextForAriaLabels()
    {
        var source = ReadWeb("wwwroot", "js", "zynora-ux-guards.js");

        Assert.Contains("function textWithoutControls", source, StringComparison.Ordinal);
        Assert.Contains("function technicalControlName", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "var contextText = textOf(context);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeBridge_ExtractsEmbeddedEmployeeBusinessFragments()
    {
        var source = ReadWeb("wwwroot", "js", "zynora-runtime-localization.js");

        Assert.Contains("function addBusinessFragments", source, StringComparison.Ordinal);
        Assert.Contains("codeThenName", source, StringComparison.Ordinal);
        Assert.Contains("nameThenCode", source, StringComparison.Ordinal);
        Assert.Contains("20260908-p6", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MajorResidualSurfaces_InheritDocumentDirection()
    {
        var disciplinary = ReadWeb("Pages", "DisciplinaryRules", "Index.cshtml");
        var payroll = ReadWeb("Pages", "Payroll", "Settings.cshtml");

        Assert.DoesNotContain(
            "<section class=\"zyw zy-scope\" dir=\"rtl\">",
            disciplinary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<section class=\"ps-page\" dir=\"rtl\">",
            payroll,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("مساء الخير، {0}", "Good evening, {0}")]
    [InlineData("تسجيل بصمة الآن — دخول", "Punch now — Check in")]
    [InlineData("تخصصية", "Specialist")]
    [InlineData("إدارة عليا", "Senior Management")]
    [InlineData("تقارير حضور جاهزة (تفصيلية وشهرية) من يوميات المحرك الرسمي + باني تقارير مخصص.",
        "Ready attendance reports (detailed and monthly) from the official engine daily records + a custom report builder.")]
    public void PostP5ResidualKeys_AreCompiledInEnglishCatalog(
        string key,
        string expected)
    {
        var catalog = LoadCatalog("SharedResource.en-US.resx");

        Assert.True(catalog.TryGetValue(key, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("مساء الخير، {0}")]
    [InlineData("تسجيل بصمة الآن — دخول")]
    [InlineData("تخصصية")]
    [InlineData("إدارة عليا")]
    public void P6Keys_HaveNonEmptyKurdishParity(string key)
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