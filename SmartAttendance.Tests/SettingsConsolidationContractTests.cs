namespace SmartAttendance.Tests;

public sealed class SettingsConsolidationContractTests
{
    [Fact]
    public void Organization_IsReadOnlyAndDelegatesEditingToSetup()
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Organization", "Index.cshtml"));
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Organization", "Index.cshtml.cs"));

        Assert.Contains("asp-page=\"/Setup/Index\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateCompany\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateBranch\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateDepartment\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateCompanyAsync", model, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateBranchAsync", model, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateDepartmentAsync", model, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsHub_DelegatesHrDetailsToHrSettingsHub()
    {
        var root = RepoRoot();
        var settings = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml"));
        var layout = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-page=\"/HrSettings/Index\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page=\"/HrSettings/ApprovalTemplates\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page=\"/HrSettings/Lookups\"", settings, StringComparison.Ordinal);
        Assert.Contains("مركز الإعدادات", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>تهيئة الشركة</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>قاموس اللغات</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>لغات بيانات الشركة</span>", layout, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }
}
