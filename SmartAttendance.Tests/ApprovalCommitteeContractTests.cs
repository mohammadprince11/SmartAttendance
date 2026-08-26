using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalCommitteeContractTests
{
    [Fact]
    public void CommitteeSchema_IsMigratedAndRequestMembershipIsFrozen()
    {
        var root = FindRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var engine = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs");

        Assert.Contains("20260826-23-approval-committees", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE ApprovalCommitteeGroups", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE ApprovalExternalCommittees", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE ApprovalRequestStepMembers", migration, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO ApprovalRequestStepMembers", engine, StringComparison.Ordinal);
        Assert.Contains("IsFrozenCommitteeMemberAsync", engine, StringComparison.Ordinal);
        Assert.Contains("FrozenCommitteeMembersAsync", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitteeAdministrationAndTemplates_AreCompanyScoped()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalCommitteeStore.cs");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Approvals", "Committees.cshtml.cs");
        var templateStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalTemplateStore.cs");
        var templatePage = Read(root, "SmartAttendance.Web", "Pages", "HrSettings", "ApprovalTemplates.cshtml");

        Assert.Contains("Demand(scope, companyId)", store, StringComparison.Ordinal);
        Assert.Contains("user.Employee.CompanyId == companyId", store, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Admin\")]", model, StringComparison.Ordinal);
        Assert.Contains("g.CompanyId=@CompanyId", templateStore, StringComparison.Ordinal);
        Assert.Contains("StepCommitteeGroup", templatePage, StringComparison.Ordinal);
        Assert.Contains("StepExternalCommittee", templatePage, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML =\n                '<span class=\"num\"", templatePage, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestSource_IsTrackedByMigrationAndAllKnownWriterFamilies()
    {
        var root = FindRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        Assert.Contains("20260826-22-approval-request-source", migration, StringComparison.Ordinal);
        Assert.Contains("N'Legacy'", migration, StringComparison.Ordinal);

        var writers = new[]
        {
            new[] { "SmartAttendance.Web", "Controllers", "Api", "MeController.cs" },
            new[] { "SmartAttendance.Web", "Infrastructure", "Hrms", "BulkRequestStore.cs" },
            new[] { "SmartAttendance.Web", "Infrastructure", "Hrms", "FinancialRequestStore.cs" },
            new[] { "SmartAttendance.Web", "Infrastructure", "Hrms", "ShiftRequestStore.cs" },
            new[] { "SmartAttendance.Web", "Pages", "EmployeePortal", "Index.cshtml.cs" },
            new[] { "SmartAttendance.Web", "Pages", "EmployeePortal", "DataChange.cshtml.cs" },
            new[] { "SmartAttendance.Web", "Pages", "MyProfile", "Index.cshtml.cs" },
            new[] { "SmartAttendance.Web", "Pages", "SelfServices", "Index.cshtml.cs" }
        };
        foreach (var writer in writers)
            Assert.Contains("RequestSource", Read(root, writer), StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
