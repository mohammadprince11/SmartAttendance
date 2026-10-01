using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiSessionAccessTests
{
    private static readonly PeopleAiSessionAccessService Service =
        new(null!, null!, null!);

    [Fact]
    public void Resolver_IntersectsPeoplePermissionsWithTenantCompanies()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "PeopleAi",
            "PeopleAiSessionAccessService.cs"));

        Assert.Contains("ICompanyScopeProvider", source, StringComparison.Ordinal);
        Assert.Contains("tenantScope.AllowedCompanyIds", source, StringComparison.Ordinal);
        Assert.Contains("companyIds = tenantCompanyIds;", source, StringComparison.Ordinal);
        Assert.Contains("companyIds.IntersectWith(tenantCompanyIds)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateLookup_FiltersTenantCompaniesAtSqlBoundary()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "PeopleIdentityDuplicateStore.cs"));

        Assert.Contains(
            "IReadOnlyCollection<int> tenantCompanyIds",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND d.CompanyId IN (",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "@TenantCompany",
            source,
            StringComparison.Ordinal);
    }

    private static EmployeeOnboardingStore.SessionRow Session(
        int companyId = 2,
        int? creatorId = 200) =>
        new(
            77,
            companyId,
            creatorId,
            "Draft",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(1));

    private static PeopleAiAccessContext Access(
        int? userId,
        bool isAdmin,
        params int[] companies) =>
        new(
            userId,
            isAdmin,
            new PeopleDataScope
            {
                AllowedCompanyIds = companies
            },
            companies.ToHashSet(),
            companies.ToHashSet());

    [Fact]
    public void WrongCompanyId_IsDenied()
    {
        Assert.False(Service.CanAccessSession(
            Session(companyId: 2),
            companyId: 1,
            Access(200, false, 1, 2)));
    }

    [Fact]
    public void CompanyOutsideScope_IsDenied()
    {
        Assert.False(Service.CanAccessSession(
            Session(companyId: 2),
            companyId: 2,
            Access(200, false, 1)));
    }

    [Fact]
    public void NonAdminWhoDidNotCreateSession_IsDenied()
    {
        Assert.False(Service.CanAccessSession(
            Session(companyId: 2, creatorId: 200),
            companyId: 2,
            Access(201, false, 2)));
    }

    [Fact]
    public void CreatorWithinScope_IsAllowed()
    {
        Assert.True(Service.CanAccessSession(
            Session(companyId: 2, creatorId: 200),
            companyId: 2,
            Access(200, false, 2)));
    }

    [Fact]
    public void AdminWithinScope_IsAllowed()
    {
        Assert.True(Service.CanAccessSession(
            Session(companyId: 2, creatorId: 200),
            companyId: 2,
            Access(999, true, 2)));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find SmartAttendance.slnx.");
    }
}
