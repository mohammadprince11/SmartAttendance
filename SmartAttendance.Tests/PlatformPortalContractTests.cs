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
        var root = RepoRoot();
        var migration = Read(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "SqlSchemaMigrator.cs");
        var companyConfiguration = Read(root, "SmartAttendance.Infrastructure", "Persistence", "Configurations", "CompanyConfiguration.cs");
        var employeeConfiguration = Read(root, "SmartAttendance.Infrastructure", "Persistence", "Configurations", "EmployeeConfiguration.cs");
        var deviceConfiguration = Read(root, "SmartAttendance.Infrastructure", "Persistence", "Configurations", "DeviceConfiguration.cs");
        var smartOnboardingReview = Read(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "SmartOnboardingReview.cshtml.cs");

        Assert.Contains("20261001-03-license-capacity-guards", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Companies", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Employees", migration, StringComparison.Ordinal);
        Assert.Contains("TR_LicenseCapacity_Devices", migration, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", migration, StringComparison.Ordinal);
        Assert.Contains("51041", migration, StringComparison.Ordinal);
        Assert.Contains("51042", migration, StringComparison.Ordinal);
        Assert.Contains("51043", migration, StringComparison.Ordinal);
        Assert.Contains("UseSqlOutputClause(false)", companyConfiguration, StringComparison.Ordinal);
        Assert.Contains("UseSqlOutputClause(false)", employeeConfiguration, StringComparison.Ordinal);
        Assert.Contains("UseSqlOutputClause(false)", deviceConfiguration, StringComparison.Ordinal);
        Assert.Contains("GetEmployeeLicenseCapacityAsync", smartOnboardingReview, StringComparison.Ordinal);
        Assert.Contains("SqlException { Number: 51042 }", smartOnboardingReview, StringComparison.Ordinal);
        Assert.Contains("حد الترخيص مكتمل", smartOnboardingReview, StringComparison.Ordinal);
        Assert.Contains("return RedirectToSelf();", smartOnboardingReview, StringComparison.Ordinal);
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

    [Fact]
    public void TenantActivation_IsServerSideToggleAndReportsFailedUpdates()
    {
        var root = RepoRoot();
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");

        Assert.DoesNotContain("name=\"active\"", page, StringComparison.Ordinal);
        Assert.Contains("var activate = !tenant.IsActive", model, StringComparison.Ordinal);
        Assert.Contains("if (tenant is null) return NotFound()", model, StringComparison.Ordinal);
        Assert.Contains("تعذر تغيير حالة المنظومة", model, StringComparison.Ordinal);
        Assert.Contains("OUTPUT inserted.Code, inserted.IsActive INTO @Changed", store, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*) FROM @Changed", store, StringComparison.Ordinal);
        Assert.Contains("string? reason, bool confirmed", model, StringComparison.Ordinal);
        Assert.Contains("name=\"reason\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"confirmed\"", page, StringComparison.Ordinal);
        Assert.Contains("@Reason", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Portal_ProvidesPlanPresetsAuditHistoryAndExpiryWarnings()
    {
        var root = RepoRoot();
        var plans = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPlanCatalog.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var details = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml");
        var index = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Index.cshtml");
        var layout = Read(root, "SmartAttendance.Web", "Pages", "Shared", "_PlatformLayout.cshtml");

        Assert.Contains("\"Basic\"", plans, StringComparison.Ordinal);
        Assert.Contains("\"Business\"", plans, StringComparison.Ordinal);
        Assert.Contains("\"Enterprise\"", plans, StringComparison.Ordinal);
        Assert.Contains("ListTenantAuditAsync", store, StringComparison.Ordinal);
        Assert.Contains("سجل التدقيق", details, StringComparison.Ordinal);
        Assert.Contains("AuditDetailsLabel(audit)", details, StringComparison.Ordinal);
        Assert.Contains("تم إنشاء المنظومة وحساب المدير الأول", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Created tenant and initial administrator", store, StringComparison.Ordinal);
        Assert.Contains("اشتراكات تحتاج متابعة", index, StringComparison.Ordinal);
        Assert.Contains("platform-portal.js", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscriptionRenewal_IsAtomicIdempotentAndAudited()
    {
        var root = RepoRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml.cs");

        Assert.Contains("20261001-04-platform-subscription-invoices", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.PlatformSubscriptionInvoices", migration, StringComparison.Ordinal);
        Assert.Contains("UX_PlatformSubscriptionInvoices_IdempotencyKey", migration, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable)", store, StringComparison.Ordinal);
        Assert.Contains("WHERE IdempotencyKey = @IdempotencyKey", store, StringComparison.Ordinal);
        Assert.Contains("license.Version = @ExpectedVersion", store, StringComparison.Ordinal);
        Assert.Contains("SubscriptionRenewed", store, StringComparison.Ordinal);
        Assert.Contains("RecordPaidRenewalAsync", model, StringComparison.Ordinal);
        Assert.Contains("تسجيل دفعة خارجية وتجديد الاشتراك", page, StringComparison.Ordinal);
        Assert.Contains("لا تنفذ أي دفع إلكتروني ولا تتصل ببوابة دفع", page, StringComparison.Ordinal);
        Assert.Contains("[\"Cheque\"]", store, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"Card\"]", store, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProfileAndTenantAdminManagement_AreControlledAndAudited()
    {
        var root = RepoRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml.cs");

        Assert.Contains("20261001-05-tenant-customer-profile", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.Tenants ADD ContactEmail", migration, StringComparison.Ordinal);
        Assert.Contains("UpdateTenantProfileAsync", store, StringComparison.Ordinal);
        Assert.Contains("GetTenantAdminAsync", store, StringComparison.Ordinal);
        Assert.Contains("ResetTenantAdminPasswordAsync", store, StringComparison.Ordinal);
        Assert.Contains("SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID())", store, StringComparison.Ordinal);
        Assert.Contains("MustChangePassword = 1", store, StringComparison.Ordinal);
        Assert.Contains("TenantAdminPasswordReset", store, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"SaveCustomerProfile\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ResetTenantAdminPassword\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"UnlockTenantAdmin\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ToggleTenantAdmin\"", page, StringComparison.Ordinal);
        Assert.Contains("OnPostResetTenantAdminPasswordAsync", model, StringComparison.Ordinal);
        Assert.DoesNotContain("@temporaryPassword", store, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TenantCreation_RequiresAnEightCharacterInitialAdminPassword()
    {
        var root = RepoRoot();
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Create.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Create.cshtml.cs");
        var index = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Index.cshtml");

        Assert.Contains("StringLength(200, MinimumLength = 8)", model, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Input.AdminPassword\" type=\"password\" minlength=\"8\" maxlength=\"200\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.PlanCode", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-platform-plan-select", page, StringComparison.Ordinal);
        Assert.Contains("DefaultPlanCode = \"Custom\"", model, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>الخطة</th>", index, StringComparison.Ordinal);
        Assert.DoesNotContain("@tenant.PlanCode", index, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantOperations_AreRecoverableAuditedAndDoNotDeleteFinancialHistory()
    {
        var root = RepoRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Platform", "PlatformPortalStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml");
        var model = Read(root, "SmartAttendance.Web", "Pages", "Platform", "Tenants", "Details.cshtml.cs");

        Assert.Contains("20261001-06-platform-tenant-operations", migration, StringComparison.Ordinal);
        Assert.Contains("UX_Tenants_PortalSubdomain", migration, StringComparison.Ordinal);
        Assert.Contains("UX_Tenants_CustomDomain", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.PlatformSubscriptionInvoices ADD VoidReason", migration, StringComparison.Ordinal);
        Assert.Contains("SetTenantArchivedAsync", store, StringComparison.Ordinal);
        Assert.Contains("SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID())", store, StringComparison.Ordinal);
        Assert.Contains("Status = N'Voided'", store, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dbo.PlatformSubscriptionInvoices", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asp-page-handler=\"SaveDomain\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"VoidInvoice\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"SetArchived\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ExportInvoices\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ExportAudit\"", page, StringComparison.Ordinal);
        Assert.Contains("OnPostSetArchivedAsync", model, StringComparison.Ordinal);
        Assert.Contains("text/csv; charset=utf-8", model, StringComparison.Ordinal);
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
