using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiFinalizationTransactionIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION")
        ?? "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;
    private int _companyId;
    private int _branchId;
    private int _departmentId;
    private readonly List<long> _sessions = [];

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await PeopleAiSettingsStore.EnsureDefaultsAsync(_db);
            _companyId = await ScalarIntAsync(
                "SELECT Id FROM dbo.Companies WHERE Code='E2E-A';");
            _branchId = await ScalarIntAsync(
                "SELECT Id FROM dbo.Branches WHERE Code='E2E-BA';");
            _departmentId = await ScalarIntAsync(
                "SELECT Id FROM dbo.Departments WHERE Code='E2E-DA';");
            _dbAvailable =
                _companyId > 0 && _branchId > 0 && _departmentId > 0;
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
            foreach (var sessionId in _sessions)
            {
                await HrmsDatabase.ExecuteAsync(
                    _db,
                    """
                    DELETE FROM dbo.PeopleAiAuditLogs
                    WHERE SessionId = @SessionId;

                    DELETE FROM dbo.EmployeeIdentityDocuments
                    WHERE SourceOnboardingDocumentId IN
                    (
                        SELECT Id FROM dbo.OnboardingDocuments
                        WHERE SessionId = @SessionId
                    );

                    DELETE FROM dbo.EmployeeDocuments
                    WHERE Notes = @Notes;

                    DELETE f
                    FROM dbo.DocumentExtractedFields f
                    JOIN dbo.DocumentExtractionRuns r
                      ON r.Id = f.ExtractionRunId
                    JOIN dbo.OnboardingDocuments d
                      ON d.Id = r.OnboardingDocumentId
                    WHERE d.SessionId = @SessionId;

                    DELETE r
                    FROM dbo.DocumentExtractionRuns r
                    JOIN dbo.OnboardingDocuments d
                      ON d.Id = r.OnboardingDocumentId
                    WHERE d.SessionId = @SessionId;

                    DELETE FROM dbo.PeopleAiJobs
                    WHERE SessionId = @SessionId;

                    DELETE FROM dbo.OnboardingDocuments
                    WHERE SessionId = @SessionId;

                    DELETE FROM dbo.ProtectedFileAssets
                    WHERE OwnerType = 'OnboardingSession'
                      AND OwnerId = @SessionId;

                    DELETE FROM dbo.EmployeeOnboardingSessions
                    WHERE Id = @SessionId;
                    """,
                    command =>
                    {
                        HrmsDatabase.AddParameter(
                            command, "@SessionId", sessionId);
                        HrmsDatabase.AddParameter(
                            command, "@Notes",
                            $"Smart Onboarding Session #{sessionId}");
                    });
            }
        }

        await _db.DisposeAsync();
    }
    [SkippableFact]
    public async Task IdentityInsertFailure_RollsBackEmployeeAndDocumentWrites()
    {
        Skip.IfNot(_dbAvailable, "Disposable E2E SQL is unavailable.");

        var sessionId = await EmployeeOnboardingStore.CreateSessionAsync(
            _db, _companyId, null, TimeSpan.FromHours(1));
        _sessions.Add(sessionId);

        var storageKey = $"itest/finalize-{Guid.NewGuid():N}.png";
        var assetId = await HrmsDatabase.ScalarAsync<long>(
            _db,
            """
            INSERT INTO dbo.ProtectedFileAssets
                (CompanyId, OwnerType, OwnerId, StorageKey,
                 OriginalFileName, MimeType, Extension, SizeBytes,
                 Sha256, SignatureValidationStatus, MalwareScanStatus)
            OUTPUT INSERTED.Id
            VALUES
                (@CompanyId, 'OnboardingSession', @SessionId, @StorageKey,
                 'itest-passport.png', 'image/png', '.png', 68,
                 REPLICATE('b', 64), 'Valid', 'Clean');
            """,
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", _companyId);
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@StorageKey", storageKey);
            });

        var documentId = await HrmsDatabase.ScalarAsync<long>(
            _db,
            """
            INSERT INTO dbo.OnboardingDocuments
                (SessionId, ProtectedFileAssetId,
                 DeclaredDocumentType, DetectedDocumentType,
                 ProcessingStatus, OriginalVerificationStatus,
                 ReviewedExpiryDate, ProcessedAt)
            OUTPUT INSERTED.Id
            VALUES
                (@SessionId, @AssetId,
                 'Passport', 'Passport',
                 'Processed', 'Verified',
                 '2035-01-02', SYSUTCDATETIME());
            """,
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@AssetId", assetId);
            });
        var extractionRunId = await HrmsDatabase.ScalarAsync<long>(
            _db,
            """
            INSERT INTO dbo.DocumentExtractionRuns
                (OnboardingDocumentId, Provider, Model,
                 ExtractorVersion, SchemaVersion, Status, CompletedAt)
            OUTPUT INSERTED.Id
            VALUES
                (@DocumentId, 'ITEST', 'TransactionFixture',
                 '1', '1', 'Completed', SYSUTCDATETIME());
            """,
            command => HrmsDatabase.AddParameter(
                command, "@DocumentId", documentId));

        var oversizedDocumentNumber = new string('X', 220);
        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            INSERT INTO dbo.DocumentExtractedFields
                (ExtractionRunId, FieldKey, RawValue, NormalizedValue,
                 ValidationStatus, ExtractionMethod,
                 ReviewStatus, ReviewedValue, ReviewedAt)
            VALUES
                (@RunId, 'DocumentNumber',
                 @Value, @Value, 'Valid', 'OCR',
                 'Accepted', @Value, SYSUTCDATETIME());
            """,
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@RunId", extractionRunId);
                HrmsDatabase.AddParameter(
                    command, "@Value", oversizedDocumentNumber);
            });

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
            UPDATE dbo.EmployeeOnboardingSessions
            SET Status = 'Ready', UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @SessionId;
            """,
            command => HrmsDatabase.AddParameter(
                command, "@SessionId", sessionId));

        var employeeNo = $"E2E-ROLLBACK-{sessionId}";
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        try
        {
            Assert.True(
                await PeopleAiFinalizationStore.TryBeginFinalizationAsync(
                    _db, sessionId, _companyId));

            var employee = new Employee
            {
                EmployeeNo = employeeNo,
                FullName = "E2E Rollback Employee",
                HireDate = new DateOnly(2026, 9, 19),
                BranchId = _branchId,
                DepartmentId = _departmentId,
                CompanyId = _companyId,
                IsCitizen = false,
                IsActive = true
            };

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();
            Assert.True(employee.Id > 0);

            var promoted = new Dictionary<long, PromotedEmployeeFile>
            {
                [assetId] = new(
                    assetId,
                    "protected://itest/rollback-passport.png",
                    $"employee-{employee.Id}/rollback-passport.png",
                    storageKey,
                    "itest-passport.png")
            };

            await Assert.ThrowsAnyAsync<Exception>(
                () => PeopleAiFinalizationStore
                    .LinkIdentityDocumentsAndCompleteAsync(
                        _db,
                        sessionId,
                        _companyId,
                        employee.Id,
                        null,
                        null,
                        null,
                        null,
                        promoted,
                        "itest"));

            await transaction.RollbackAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        _db.ChangeTracker.Clear();
        Assert.Equal(0, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.Employees WHERE EmployeeNo=@EmployeeNo;",
            ("@EmployeeNo", employeeNo)));

        Assert.Equal(1, await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.EmployeeOnboardingSessions
            WHERE Id=@SessionId
              AND Status='Ready'
              AND CreatedEmployeeId IS NULL;
            """,
            ("@SessionId", sessionId)));

        Assert.Equal(0, await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.EmployeeDocuments
            WHERE Notes=@Notes;
            """,
            ("@Notes", $"Smart Onboarding Session #{sessionId}")));

        Assert.Equal(0, await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.EmployeeIdentityDocuments
            WHERE SourceOnboardingDocumentId=@DocumentId;
            """,
            ("@DocumentId", documentId)));

        Assert.Equal(0, await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.PeopleAiAuditLogs
            WHERE SessionId=@SessionId
              AND Operation='EmployeeCreated';
            """,
            ("@SessionId", sessionId)));
    }
    private async Task<int> ScalarIntAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = parameter.Name;
            p.Value = parameter.Value;
            command.Parameters.Add(p);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
