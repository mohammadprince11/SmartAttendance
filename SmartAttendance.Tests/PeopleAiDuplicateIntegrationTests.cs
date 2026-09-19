using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiDuplicateIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;
    private int _companyA;
    private int _companyB;
    private int _employeeA;
    private int _employeeB;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await PeopleAiSettingsStore.EnsureDefaultsAsync(_db);
            _companyA = await ScalarIntAsync(
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-A';");
            _companyB = await ScalarIntAsync(
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-B';");
            _employeeA = await ScalarIntAsync(
                "SELECT Id FROM dbo.Employees WHERE EmployeeNo='E2E-001';");
            _employeeB = await ScalarIntAsync(
                "SELECT Id FROM dbo.Employees WHERE EmployeeNo='E2E-002';");
            await CleanupAsync();
            _dbAvailable =
                _companyA > 0 && _companyB > 0 &&
                _employeeA > 0 && _employeeB > 0;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dbAvailable)
        {
            await CleanupAsync();
        }
        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task CurrentCompany_MatchesNationalIdAndPassport()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");
        await SavePolicyAsync(PeopleAiDuplicateScope.CurrentCompany);
        await InsertIdentityAsync(
            _employeeA, _companyA, "NationalId", "ITEST-NAT-A");
        await InsertIdentityAsync(
            _employeeA, _companyA, "Passport", "ITEST-PASS-A");
        var scope = new PeopleDataScope
        {
            AllowedCompanyIds = [_companyA]
        };

        var national = await PeopleIdentityDuplicateStore.FindAsync(
            _db, scope, _companyA, "NationalId", "itest nat a");
        var passport = await PeopleIdentityDuplicateStore.FindAsync(
            _db, scope, _companyA, "Passport", "itest-pass-a");

        Assert.True(national.HasDuplicate);
        Assert.True(national.RequiresReview);
        Assert.Single(national.Candidates);
        Assert.Equal(_employeeA, national.Candidates[0].EmployeeId);
        Assert.True(national.Candidates[0].IsVisibleToRequester);

        Assert.True(passport.HasDuplicate);
        Assert.Single(passport.Candidates);
        Assert.Equal(_employeeA, passport.Candidates[0].EmployeeId);
    }

    [SkippableFact]
    public async Task AuthorizedCompanies_MatchesVisibleCrossCompanyIdentity()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");
        await SavePolicyAsync(PeopleAiDuplicateScope.AuthorizedCompanies);
        await InsertIdentityAsync(
            _employeeB, _companyB, "Passport", "ITEST-PASS-B");

        var scope = new PeopleDataScope
        {
            AllowedCompanyIds = [_companyA, _companyB]
        };

        var result = await PeopleIdentityDuplicateStore.FindAsync(
            _db, scope, _companyA, "Passport", "itest pass b");
        Assert.True(result.HasDuplicate);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(_employeeB, candidate.EmployeeId);
        Assert.Equal(_companyB, candidate.CompanyId);
        Assert.True(candidate.IsVisibleToRequester);
    }

    [SkippableFact]
    public async Task OutsideAuthorizedScope_IsHiddenOrRedactedByPolicy()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");
        await InsertIdentityAsync(
            _employeeB, _companyB, "NationalId", "ITEST-NAT-B");

        var scope = new PeopleDataScope
        {
            AllowedCompanyIds = [_companyA]
        };

        await SavePolicyAsync(PeopleAiDuplicateScope.AuthorizedCompanies);
        var authorizedOnly = await PeopleIdentityDuplicateStore.FindAsync(
            _db, scope, _companyA, "NationalId", "ITEST-NAT-B");
        Assert.False(authorizedOnly.HasDuplicate);

        await SavePolicyAsync(PeopleAiDuplicateScope.WholeTenant);
        var tenantWide = await PeopleIdentityDuplicateStore.FindAsync(
            _db, scope, _companyA, "NationalId", "ITEST-NAT-B");

        var redacted = Assert.Single(tenantWide.Candidates);
        Assert.False(redacted.IsVisibleToRequester);
        Assert.Equal(0, redacted.EmployeeId);
        Assert.Equal(0, redacted.CompanyId);
        Assert.Contains("غير مصرح", redacted.DisplayName);
    }
    private Task SavePolicyAsync(PeopleAiDuplicateScope scope) =>
        PeopleAiSettingsStore.SaveAsync(
            _db,
            new CompanyPeopleAiPolicy(
                _companyA,
                scope,
                PeopleAiDuplicateAction.RequireReview,
                PeopleAiReviewerMode.AdminOrCreatorWithPermission,
                ["ar", "en"],
                CloudProcessingAllowed: false,
                IsEnabled: true),
            "itest");

    private Task InsertIdentityAsync(
        int employeeId,
        int companyId,
        string type,
        string number) =>
        HrmsDatabase.ExecuteAsync(
            _db,
            """
            INSERT INTO dbo.EmployeeIdentityDocuments
                (CompanyId, EmployeeId, DocumentType,
                 DocumentNumber, NormalizedDocumentNumber)
            VALUES
                (@CompanyId, @EmployeeId, @DocumentType,
                 @DocumentNumber, @NormalizedNumber);
            """,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@DocumentType", type);
                HrmsDatabase.AddParameter(command, "@DocumentNumber", number);
                HrmsDatabase.AddParameter(
                    command, "@NormalizedNumber",
                    IdentityDocumentNormalizer.NormalizeNumber(number));
            });
    private Task CleanupAsync() =>
        HrmsDatabase.ExecuteAsync(
            _db,
            """
            DELETE FROM dbo.EmployeeIdentityDocuments
            WHERE DocumentNumber LIKE 'ITEST-%';
            """);

    private async Task<int> ScalarIntAsync(string sql)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
