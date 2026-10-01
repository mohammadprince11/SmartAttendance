using Xunit;

namespace SmartAttendance.Tests;

public sealed class DataLanguagesEmptyCompanyContractTests
{
    [Fact]
    public void DataLanguages_RemainsReachable_WhenNoCompanyExists()
    {
        var root = FindRoot();

        var model = File.ReadAllText(
            Path.Combine(
                root,
                "SmartAttendance.Web",
                "Pages",
                "Settings",
                "DataLanguages.cshtml.cs"));

        var page = File.ReadAllText(
            Path.Combine(
                root,
                "SmartAttendance.Web",
                "Pages",
                "Settings",
                "DataLanguages.cshtml"));

        Assert.Contains(
            "if (Companies.Count == 0)",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "return Page();",
            model,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "if (CompanyId is null) return NotFound();",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "@if (Model.Companies.Count == 0)",
            page,
            StringComparison.Ordinal);

        Assert.Contains(
            "إنشاء شركة",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DataLanguages_ReturnsToOnboardingStepsAfterSave()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Settings", "DataLanguages.cshtml.cs"));
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Settings", "DataLanguages.cshtml"));
        var setup = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Setup", "Index.cshtml"));

        Assert.Contains("[BindProperty(SupportsGet = true)] public bool Onboarding", model, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(\"/Setup/Index\", new { companyId, onboarding = true })", model, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Onboarding\"", page, StringComparison.Ordinal);
        Assert.Contains("الرجوع إلى خطوات التأسيس", page, StringComparison.Ordinal);
        Assert.Contains("asp-route-onboarding=\"true\"", setup, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(
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
