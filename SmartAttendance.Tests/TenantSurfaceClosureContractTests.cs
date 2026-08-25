namespace SmartAttendance.Tests;

public sealed class TenantSurfaceClosureContractTests
{
    [Fact]
    public void EmployeeOwnedDocumentAndFormStores_FilterByCompanyBeforeMaterialization()
    {
        var documentRequests = ReadWeb("Infrastructure", "Hrms", "DocumentRequestStore.cs");
        var generatedDocuments = ReadWeb("Infrastructure", "Hrms", "DocumentTemplateStore.cs");
        var submissions = ReadWeb("Infrastructure", "Hrms", "FormSubmissionStore.cs");

        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", documentRequests);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", generatedDocuments);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", submissions);
        Assert.Contains("CanAccessOwnedRowAsync", submissions);
    }

    [Fact]
    public void DocumentAndFormPages_PropagateTheCurrentCompanyScopeToEverySensitiveAction()
    {
        var requests = ReadWeb("Pages", "Documents", "Requests.cshtml.cs");
        var generate = ReadWeb("Pages", "Documents", "Generate.cshtml.cs");
        var submissions = ReadWeb("Pages", "Forms", "Submissions.cshtml.cs");

        Assert.Contains("ICompanyScopeProvider", requests);
        Assert.Contains("scope: scope", requests);
        Assert.Contains("CanAccessEmployeeAsync", generate);
        Assert.Contains("CanAccessOwnedRowAsync", generate);
        Assert.Contains("scope: scope", submissions);
    }

    [Fact]
    public void EngagementManagement_IsScopedForListsTargetsAndMutations()
    {
        var service = Read("SmartAttendance.Infrastructure", "Services", "AnnouncementService.cs");
        var shared = ReadWeb("Pages", "Engagement", "EngagementPageModel.cs");
        var announcements = ReadWeb("Pages", "Engagement", "Announcements.cshtml.cs");
        var feedback = ReadWeb("Pages", "Engagement", "Feedback.cshtml.cs");

        Assert.Contains("AllowedCompanyIds", service);
        Assert.Contains("group.AudienceRules.Any", service);
        Assert.Contains("IsTargetWithinCompanyScopeAsync", shared);
        Assert.Contains("CanManageAnnouncementAsync", announcements);
        Assert.Contains("CanAccessOwnedRowAsync", feedback);
    }

    [Theory]
    [InlineData("Pages", "CompanyDocuments", "Index.cshtml.cs")]
    [InlineData("Pages", "Documents", "Templates.cshtml.cs")]
    [InlineData("Pages", "Forms", "Index.cshtml.cs")]
    public void GlobalConfigurationSurfaces_AreExplicitlyAdministratorOnly(params string[] parts)
    {
        Assert.Contains("[Authorize(Roles = RoleRouteCatalog.Admin)]", ReadWeb(parts));
    }

    private static string ReadWeb(params string[] parts) =>
        Read(new[] { "SmartAttendance.Web" }.Concat(parts).ToArray());

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
