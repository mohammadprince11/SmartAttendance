using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleIdentityBootstrap
{
    private sealed record LegacyIdentityRow(
        int EmployeeId,
        int CompanyId,
        string NationalId,
        string PassportNo);

    public static async Task EnsureLegacyIdentityRowsAsync(ApplicationDbContext db)
    {
        await PeopleAiSchema.VerifyAsync(db);

        var employees = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, ISNULL(CompanyId, 0) AS CompanyId,
       ISNULL(NationalId, '') AS NationalId,
       ISNULL(PassportNo, '') AS PassportNo
FROM dbo.Employees
WHERE ISNULL(IsDeleted, 0) = 0
  AND (NULLIF(LTRIM(RTRIM(NationalId)), '') IS NOT NULL
       OR NULLIF(LTRIM(RTRIM(PassportNo)), '') IS NOT NULL);
""",
            command => { },
            reader => new LegacyIdentityRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "NationalId"),
                HrmsDatabase.GetString(reader, "PassportNo")));

        foreach (var employee in employees.Where(x => x.CompanyId > 0))
        {
            await EnsureDocumentAsync(
                db,
                employee,
                PeopleAiDocumentTypes.NationalId,
                employee.NationalId);

            await EnsureDocumentAsync(
                db,
                employee,
                PeopleAiDocumentTypes.Passport,
                employee.PassportNo);
        }
    }

    public static async Task<bool> EnsureEmployeeIdentityRowsAsync(
        ApplicationDbContext db,
        int employeeId)
    {
        if (employeeId <= 0)
        {
            return false;
        }

        await PeopleAiSchema.VerifyAsync(db);

        var employees = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1 Id, ISNULL(CompanyId, 0) AS CompanyId,
       ISNULL(NationalId, '') AS NationalId,
       ISNULL(PassportNo, '') AS PassportNo
FROM dbo.Employees
WHERE Id = @EmployeeId
  AND ISNULL(IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@EmployeeId",
                employeeId),
            reader => new LegacyIdentityRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "NationalId"),
                HrmsDatabase.GetString(reader, "PassportNo")));

        var employee = employees.FirstOrDefault();
        if (employee is null || employee.CompanyId <= 0)
        {
            return false;
        }

        await EnsureDocumentAsync(
            db,
            employee,
            PeopleAiDocumentTypes.NationalId,
            employee.NationalId);
        await EnsureDocumentAsync(
            db,
            employee,
            PeopleAiDocumentTypes.Passport,
            employee.PassportNo);

        return true;
    }

    public static async Task<bool> SetFamilyNumberAsync(
        ApplicationDbContext db,
        int employeeId,
        string? familyNumber,
        long preferredIdentityDocumentId = 0)
    {
        var normalized = string.IsNullOrWhiteSpace(familyNumber)
            ? null
            : familyNumber.Trim();

        await EnsureEmployeeIdentityRowsAsync(db, employeeId);

        var identityId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
SELECT TOP 1 Id
FROM dbo.EmployeeIdentityDocuments
WHERE EmployeeId = @EmployeeId
  AND DocumentType = N'NationalId'
  AND IsCurrent = 1
ORDER BY
    CASE WHEN Id = @PreferredId THEN 0 ELSE 1 END,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(FamilyNumber)), '') IS NOT NULL THEN 0
        ELSE 1
    END,
    CASE WHEN SourceOnboardingDocumentId IS NOT NULL THEN 0 ELSE 1 END,
    Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@PreferredId",
                    preferredIdentityDocumentId);
            });

        if (identityId <= 0)
        {
            return normalized is null;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.EmployeeIdentityDocuments
SET FamilyNumber = @FamilyNumber,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @IdentityId
  AND EmployeeId = @EmployeeId
  AND DocumentType = N'NationalId';
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@FamilyNumber",
                    (object?)normalized ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@IdentityId",
                    identityId);
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
            });

        return true;
    }

    private static async Task EnsureDocumentAsync(
        ApplicationDbContext db,
        LegacyIdentityRow employee,
        string documentType,
        string documentNumber)
    {
        var normalized = IdentityDocumentNormalizer.NormalizeNumber(documentNumber);
        if (normalized.Length == 0)
        {
            return;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.EmployeeIdentityDocuments
    WHERE EmployeeId = @EmployeeId
      AND DocumentType = @DocumentType
      AND NormalizedDocumentNumber = @NormalizedNumber
      AND IsCurrent = 1
)
BEGIN
    INSERT INTO dbo.EmployeeIdentityDocuments
        (CompanyId, EmployeeId, DocumentType, DocumentNumber,
         NormalizedDocumentNumber, ExtractionStatus, VerificationStatus,
         OriginalVerificationStatus, IsCurrent)
    VALUES
        (@CompanyId, @EmployeeId, @DocumentType, @DocumentNumber,
         @NormalizedNumber, 'NotProcessed', 'LegacyImported',
         'NotSeen', 1);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", employee.CompanyId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employee.EmployeeId);
                HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                HrmsDatabase.AddParameter(command, "@DocumentNumber", documentNumber.Trim());
                HrmsDatabase.AddParameter(command, "@NormalizedNumber", normalized);
            });
    }
}
