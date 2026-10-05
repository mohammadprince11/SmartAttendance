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
        var styles = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "css", "pages", "organization-hub.css"));

        Assert.Contains("asp-page=\"/Setup/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("css/pages/organization-hub.css", page, StringComparison.Ordinal);
        Assert.Contains("class=\"org-summary\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"hrms-tabs org-tabs\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("index-81f0f3e349.css", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateCompany\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateBranch\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"CreateDepartment\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateCompanyAsync", model, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateBranchAsync", model, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostCreateDepartmentAsync", model, StringComparison.Ordinal);
        Assert.Contains(".org-company-card__counts", styles, StringComparison.Ordinal);
        Assert.Contains("border: 0;", styles, StringComparison.Ordinal);
        Assert.Contains("background: transparent;", styles, StringComparison.Ordinal);
        Assert.Contains(".org-company-card__counts > div", styles, StringComparison.Ordinal);
        Assert.Contains("min-block-size: 64px;", styles, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--sa-border);", styles, StringComparison.Ordinal);
        Assert.Contains("background: var(--sa-surface-2);", styles, StringComparison.Ordinal);
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
        Assert.Contains("asp-page=\"/Settings/Index\"", layout, StringComparison.Ordinal);
        Assert.Contains("<span>الإعدادات</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>تهيئة الشركة</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>قاموس اللغات</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>لغات بيانات الشركة</span>", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsHub_UsesPanelsInsteadOfScrollJumpLinks()
    {
        var root = RepoRoot();
        var settings = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js", "settings-hub.js"));
        var styles = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "css", "zynora-administration.css"));

        Assert.Contains("role=\"tablist\"", settings, StringComparison.Ordinal);
        Assert.Contains("data-settings-tab=\"company-settings\"", settings, StringComparison.Ordinal);
        Assert.Contains("role=\"tabpanel\"", settings, StringComparison.Ordinal);
        Assert.Contains("data-settings-panel", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"#company-settings\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollIntoView", script, StringComparison.Ordinal);
        Assert.Contains("panel.hidden = panel !== selectedPanel", script, StringComparison.Ordinal);
        Assert.Contains(".zy-settings-section[hidden]", styles, StringComparison.Ordinal);
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
