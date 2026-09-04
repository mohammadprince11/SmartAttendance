using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalAdministrationSurfaceTests
{
    [Fact]
    public void MainNavigation_ExposesApprovalsSettingsAuditAndPermissionAwareSearch()
    {
        var root = FindRoot();
        var layout = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Shared", "_Layout.cshtml"));
        var search = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "wwwroot", "js", "zynora-command-search.js"));

        Assert.Contains("data-nav-area=\"approvals\"", layout, StringComparison.Ordinal);
        Assert.Contains("/Approvals/Reports", layout, StringComparison.Ordinal);
        Assert.Contains("/Approvals/Committees", layout, StringComparison.Ordinal);
        Assert.Contains("data-nav-area=\"settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("/AuditLogs/Index", layout, StringComparison.Ordinal);
        Assert.Contains("data-zy-command-search", layout, StringComparison.Ordinal);
        Assert.Contains(".zynora-nav a[href]", search, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", search, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovalCenter_ExposesRequestSourcesAndScopedDecisionHistory()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml"));
        var engine = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs"));

        Assert.Contains("r.RequestSource = @Source", model, StringComparison.Ordinal);
        Assert.Contains("GetHistoriesAsync(_dbContext, scope", model, StringComparison.Ordinal);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", engine, StringComparison.Ordinal);
        Assert.Contains("apv-history", page, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsHub_IsAdminOnlyAndLinksToRealConfigurationSources()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml"));
        var auditModel = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "AuditLogs", "Index.cshtml.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", model, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Admin\")]", auditModel, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Setup/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/AccessRoles/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/AuditLogs/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/HrSettings/ApprovalTemplates\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalReport_IsTenantScopedParameterizedAndUsesSharedSafeExporter()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Approvals", "Reports.cshtml.cs"));

        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", model, StringComparison.Ordinal);
        Assert.Contains("HrmsDatabase.AddParameter(command, \"@Status\"", model, StringComparison.Ordinal);
        Assert.Contains("HrmsDatabase.AddParameter(command, \"@RequestType\"", model, StringComparison.Ordinal);
        Assert.Contains("ReportExportService.Build", model, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreatedAsync", model, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalDetails_RenderFrozenWorkflowAndDelegationHasStableAnchor()
    {
        var root = FindRoot();
        var approvals = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml"));
        var templates = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "HrSettings", "ApprovalTemplates.cshtml"));

        Assert.Contains("flow?.Steps.Count", approvals, StringComparison.Ordinal);
        Assert.Contains("apv-flow-list", approvals, StringComparison.Ordinal);
        Assert.Contains("id=\"approval-delegations\"", templates, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
