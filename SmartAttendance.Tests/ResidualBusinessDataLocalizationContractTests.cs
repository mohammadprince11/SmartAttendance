using Xunit;

namespace SmartAttendance.Tests;

public sealed class ResidualBusinessDataLocalizationContractTests
{
    [Fact]
    public void PayrollProvisions_UsesLocalizedEmployeeBusinessData()
    {
        var root = FindRoot();
        var source = Read(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "ProvisionCalculator.cs");

        Assert.Contains(
            "GetEmployeeBusinessDataAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "localizedDisplay!.DepartmentName",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "localizedDisplay!.BranchName",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UserAccess_LocalizesEmployeeDerivedRowsWithoutOverwritingCustomSystemNames()
    {
        var root = FindRoot();
        var source = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "UserAccess",
            "Index.cshtml.cs");

        Assert.Contains(
            "LocalizeIdentityRowsAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeEmployeeOptionsAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "legacyEmployeeName",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "IdentityLinkStatus.NoAccount",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_LocalizesSelectorsAndStructureRowsButDoesNotRewriteProfileName()
    {
        var root = FindRoot();
        var source = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Setup",
            "Index.cshtml.cs");

        Assert.Contains(
            "LocalizeCompaniesAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeBranchesAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "LocalizeDepartmentsAsync",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Profile.Name = selectedCompanyName",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Roster_LocalizesVisibleEmployeePageRows()
    {
        var root = FindRoot();
        var source = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Roster",
            "Index.cshtml.cs");

        Assert.Contains(
            "localizedRosterEmployees",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "display.FullName",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "display.DepartmentName",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Positions_LocalizesDisplayAfterEditModelCapturesBaseValues()
    {
        var root = FindRoot();
        var source = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Positions",
            "Index.cshtml.cs");

        var translationLoad = source.IndexOf(
            "await LoadPositionTranslationsAsync",
            StringComparison.Ordinal);

        var displayLoad = source.IndexOf(
            "await LocalizePositionDisplayRowsAsync",
            StringComparison.Ordinal);

        Assert.True(
            translationLoad >= 0 &&
            displayLoad > translationLoad,
            "Display localization must run after edit translation values are loaded.");

        Assert.Contains(
            "GetCompanyNamesAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetEntityNamesAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_LocalizesDemoAndEmployeeDataAndDoesNotForceInnerRtl()
    {
        var root = FindRoot();

        var model = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "EmployeePortal",
            "Index.cshtml.cs");

        var view = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "EmployeePortal",
            "Index.cshtml");

        Assert.Contains(
            "GetCatalogAsync(",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "CultureInfo.CurrentUICulture.Name",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetEmployeeBusinessDataAsync",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "Employee = Employee with",
            model,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "<section class=\"nxex-page\" dir=\"rtl\"",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "<section class=\"nxex-page\" data-nxex-page",
            view,
            StringComparison.Ordinal);
    }

    private static string Read(
        string root,
        params string[] parts) =>
        File.ReadAllText(
            Path.Combine(
                new[] { root }
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