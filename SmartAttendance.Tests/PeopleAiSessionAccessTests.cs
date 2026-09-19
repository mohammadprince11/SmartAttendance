using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiSessionAccessTests
{
    private static readonly PeopleAiSessionAccessService Service =
        new(null!, null!);

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
}
