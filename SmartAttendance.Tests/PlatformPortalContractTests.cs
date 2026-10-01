using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SmartAttendance.Web.Infrastructure.Platform;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PlatformPortalContractTests
{
    [Theory]
    [InlineData("Active", "2026-10-01", null, null, "2026-10-01", true)]
    [InlineData("Trial", "2026-10-01", "2026-10-10", "2026-10-17", "2026-10-12", true)]
    [InlineData("Active", "2026-10-01", "2026-10-10", "2026-10-11", "2026-10-12", false)]
    [InlineData("Suspended", "2026-10-01", null, null, "2026-10-01", false)]
    [InlineData("Active", "2026-10-02", null, null, "2026-10-01", false)]
    public void LicensePolicy_ControlsTenantAccess(
        string status,
        string starts,
        string? expires,
        string? grace,
        string now,
        bool expected)
    {
        Assert.Equal(expected, PlatformLicensePolicy.AllowsAccess(
            status,
            DateTime.Parse(starts),
            expires is null ? null : DateTime.Parse(expires),
            grace is null ? null : DateTime.Parse(grace),
            DateTime.Parse(now)));
    }

    [Fact]
    public void HostPolicy_DefaultsToLoopbackAndSupportsDedicatedDomain()
    {
        var localConfiguration = new ConfigurationBuilder().Build();
        Assert.True(PlatformHostPolicy.IsAllowedHost(new HostString("127.0.0.1", 5091), localConfiguration));
        Assert.False(PlatformHostPolicy.IsAllowedHost(new HostString("customer.example.com"), localConfiguration));

        var dedicatedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPortal:AllowedHosts:0"] = "admin.zynorahr.com"
            })
            .Build();
        Assert.True(PlatformHostPolicy.IsAllowedHost(new HostString("admin.zynorahr.com"), dedicatedConfiguration));
        Assert.False(PlatformHostPolicy.IsAllowedHost(new HostString("zynorahr.com"), dedicatedConfiguration));
    }

    [Fact]
    public void Portal_UsesSeparateAuthenticationAndControlledSchema()
    {
        var root = RepoRoot();
        var program = Read(root, "SmartAttendance.Web", "Program.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var login = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Login.cshtml.cs");

        Assert.Contains("PlatformAuthenticationDefaults.Scheme", program, StringComparison.Ordinal);
        Assert.Contains("20261001-02-platform-portal-licenses", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.PlatformOwners", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.TenantLicenses", migration, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", store, StringComparison.Ordinal);
        Assert.Contains("Environment.GetEnvironmentVariable", store, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN dbo.Branches b ON b.Id = d.BranchId", store, StringComparison.Ordinal);
        Assert.Contains("PlatformAuthenticationDefaults.Scheme", login, StringComparison.Ordinal);
    }

    [Fact]
    public void Portal_LoginIsPublicAndCompanyRoleGuardDoesNotOwnPlatformRoutes()
    {
        Assert.Equal(PathAccessClass.Public, PublicPathPolicy.Classify("/Platform/Login"));

        var middleware = Read(
            RepoRoot(),
            "SmartAttendance.Web",
            "Infrastructure",
            "Security",
            "RoleSecurityMiddleware.cs");

        Assert.Contains("path.StartsWith(\"/platform/\")", middleware, StringComparison.Ordinal);
        Assert.Contains("PlatformOwnerOnly", middleware, StringComparison.Ordinal);
    }

    [Fact]
    public void Portal_SearchesTenantAndCompanyNamesAndCodesWithParameters()
    {
        var root = RepoRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Index.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Index.cshtml.cs");

        Assert.Contains("searchCompany.TenantId = t.Id", store, StringComparison.Ordinal);
        Assert.Contains("searchCompany.Name LIKE @SearchPattern", store, StringComparison.Ordinal);
        Assert.Contains("searchCompany.Code LIKE @SearchPattern", store, StringComparison.Ordinal);
        Assert.Contains("\"@SearchPattern\"", store, StringComparison.Ordinal);
        Assert.Contains("EscapeLikePattern(normalizedSearch)", store, StringComparison.Ordinal);
        Assert.Contains("BindProperty(SupportsGet = true, Name = \"q\")", model, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", page, StringComparison.Ordinal);
        Assert.Contains("اسم العميل، اسم الشركة أو الكود", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Payroll/SalarySlip", "Payroll")]
    [InlineData("/Devices", "Attendance")]
    [InlineData("/ShiftAssignments/Create", "Attendance")]
    [InlineData("/EmployeePortal", "SelfService")]
    [InlineData("/HrSettings/PeopleAI", "PeopleAI")]
    [InlineData("/Employees/SmartOnboardingReview", "PeopleAI")]
    [InlineData("/Settings", null)]
    public void LicensedModule_IsResolvedFromServerRoute(string path, string? expected)
    {
        Assert.Equal(expected, TenantModuleAccess.ResolveModule(path));
    }

    [Fact]
    public void CapacityLimits_AreEnforcedInTheDatabaseForEveryWritePath()
    {
        var migration = Read(
            RepoRoot(),
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "SqlSchemaMigrator.cs");

        Assert.Contains("20261001-03-license-capacity-guards", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Companies", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Employees", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Devices", migration, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", migration, StringComparison.Ordinal);
        Assert.Contains("51041", migration, StringComparison.Ordinal);
        Assert.Contains("51042", migration, StringComparison.Ordinal);
        Assert.Contains("51043", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileModule_IsRequiredForTokenIssueAndValidation()
    {
        var root = RepoRoot();
        var auth = Read(root, "SmartAttendance.Web", "Controllers", "Api", "AuthController.cs");
        var tokens = Read(root, "SmartAttendance.Web", "Infrastructure", "Api", "ApiTokenStore.cs");

        Assert.Contains("TenantModuleAccess.IsEnabledAsync(_db, tenant!.Id, \"Mobile\")", auth, StringComparison.Ordinal);
        Assert.Contains("STRING_SPLIT(license.EnabledModulesCsv", tokens, StringComparison.Ordinal);
        Assert.Contains("N'Mobile'", tokens, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
