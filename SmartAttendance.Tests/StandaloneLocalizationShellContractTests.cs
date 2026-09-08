using Xunit;

namespace SmartAttendance.Tests;

public sealed class StandaloneLocalizationShellContractTests
{
    [Fact]
    public void StandaloneShells_AreCultureAware_AndLoadRuntimeLocalization()
    {
        var root = FindRoot();

        var imports = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "_ViewImports.cshtml"));

        var tagHelpers = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Localization",
            "LocalizationShellTagHelpers.cs"));

        Assert.Contains(
            "@addTagHelper SmartAttendance.Web.Infrastructure.Localization.LocalizationHtmlTagHelper, SmartAttendance.Web",
            imports,
            StringComparison.Ordinal);

        Assert.Contains(
            "@addTagHelper SmartAttendance.Web.Infrastructure.Localization.LocalizationRuntimeScriptTagHelper, SmartAttendance.Web",
            imports,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "@addTagHelper *, SmartAttendance.Web",
            imports,
            StringComparison.Ordinal);

        Assert.Contains(
            "CultureInfo.CurrentUICulture",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "FindLanguageAsync",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "output.Attributes.SetAttribute(",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"lang\"",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dir\"",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/EmployeePortal\"",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/Verify\"",
            tagHelpers,
            StringComparison.Ordinal);

        Assert.Contains(
            "zynora-runtime-localization.js",
            tagHelpers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyPage_UsesCurrentUiCulture_AndServerSideDictionary()
    {
        var root = FindRoot();

        var verify = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Verify.cshtml"));

        Assert.Contains(
            "CultureInfo.CurrentUICulture",
            verify,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizationDictionary.FindLanguageAsync",
            verify,
            StringComparison.Ordinal);

        Assert.Contains(
            "<html lang=\"@currentCulture.Name\" dir=\"@currentDirection\">",
            verify,
            StringComparison.Ordinal);

        Assert.Contains(
            "T[\"التحقق من وثيقة\"]",
            verify,
            StringComparison.Ordinal);

        Assert.Contains(
            "T[Model.Message].Value",
            verify,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "<html lang=\"ar\" dir=\"rtl\">",
            verify,
            StringComparison.Ordinal);
    }

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