using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleAiFinalizationStore
{
    private sealed record DocumentSource(
        long DocumentId,
        long AssetId,
        string DocumentType,
        string OriginalVerificationStatus,
        int? OriginalVerifiedBySystemUserId,
        DateTime? OriginalVerifiedAt,
        DateOnly? ReviewedExpiryDate);

    private sealed record AcceptedField(
        long DocumentId,
        string FieldKey,
        string Value);

    public static async Task<bool> TryBeginFinalizationAsync(
        ApplicationDbContext db,
        long sessionId,
        int companyId)
    {
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
UPDATE dbo.EmployeeOnboardingSessions WITH (UPDLOCK, ROWLOCK)
SET Status = 'Completing',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND CompanyId = @CompanyId
  AND Status = 'Ready'
  AND CreatedEmployeeId IS NULL;

SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            });

        return affected == 1;
    }

    public static async Task LinkIdentityDocumentsAndCompleteAsync(
        ApplicationDbContext db,
        long sessionId,
        int companyId,
        int employeeId,
        int? reviewerSystemUserId,
        string? nationalIdOverride,
        string? familyNumberOverride,
        string? passportOverride,
        IReadOnlyDictionary<long, PromotedEmployeeFile> promotedFiles,
        string uploadedBy)
    {
        var sessionCompanyId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT CompanyId
FROM dbo.EmployeeOnboardingSessions WITH (UPDLOCK, ROWLOCK)
WHERE Id = @SessionId
  AND Status = 'Completing'
  AND CreatedEmployeeId IS NULL;
""",
            command => HrmsDatabase.AddParameter(
                command, "@SessionId", sessionId));

        if (sessionCompanyId != companyId)
        {
            throw new InvalidOperationException(
                "Onboarding session is not ready for finalization.");
        }

        var documents = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, ProtectedFileAssetId,
       COALESCE(NULLIF(DetectedDocumentType, ''),
                NULLIF(DeclaredDocumentType, ''),
                'Unknown') AS DocumentType,
       OriginalVerificationStatus,
       OriginalVerifiedBySystemUserId,
       OriginalVerifiedAt,
       ReviewedExpiryDate
FROM dbo.OnboardingDocuments
WHERE SessionId = @SessionId;
""",
            command => HrmsDatabase.AddParameter(
                command, "@SessionId", sessionId),
            reader => new DocumentSource(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetLong(reader, "ProtectedFileAssetId"),
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "OriginalVerificationStatus"),
                HrmsDatabase.GetNullableInt(reader, "OriginalVerifiedBySystemUserId"),
                HrmsDatabase.GetDateTime(reader, "OriginalVerifiedAt"),
                HrmsDatabase.GetDateOnly(reader, "ReviewedExpiryDate")));

        var fields = await HrmsDatabase.QueryAsync(
            db,
            """
WITH LatestRuns AS
(
    SELECT r.Id, r.OnboardingDocumentId,
           ROW_NUMBER() OVER(
               PARTITION BY r.OnboardingDocumentId
               ORDER BY r.Id DESC) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
)
SELECT lr.OnboardingDocumentId AS DocumentId,
       f.FieldKey,
       COALESCE(NULLIF(f.ReviewedValue, ''),
                NULLIF(f.NormalizedValue, ''),
                f.RawValue) AS FinalValue
FROM LatestRuns lr
JOIN dbo.DocumentExtractedFields f
  ON f.ExtractionRunId = lr.Id
WHERE lr.rn = 1
  AND f.ReviewStatus IN ('Accepted', 'Modified')
  AND f.FieldKey NOT LIKE 'OCR.%'
  AND f.FieldKey NOT LIKE 'MRZ.Check.%';
""",
            command => HrmsDatabase.AddParameter(
                command, "@SessionId", sessionId),
            reader => new AcceptedField(
                HrmsDatabase.GetLong(reader, "DocumentId"),
                HrmsDatabase.GetString(reader, "FieldKey"),
                HrmsDatabase.GetString(reader, "FinalValue")));

        foreach (var document in documents)
        {
            if (!promotedFiles.TryGetValue(document.AssetId, out var promoted))
            {
                throw new InvalidOperationException(
                    $"Protected onboarding asset {document.AssetId} was not promoted.");
            }

            var docFields = fields
                .Where(x => x.DocumentId == document.DocumentId)
                .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase);

            var documentNumber = ResolveDocumentNumber(
                document.DocumentType,
                docFields,
                nationalIdOverride,
                passportOverride);

            var normalizedNumber =
                IdentityDocumentNormalizer.NormalizeNumber(documentNumber);
            var nationalNumber = document.DocumentType.Equals(
                    PeopleAiDocumentTypes.NationalId,
                    StringComparison.OrdinalIgnoreCase)
                ? (!string.IsNullOrWhiteSpace(nationalIdOverride)
                    ? nationalIdOverride.Trim()
                    : GetValue(docFields, "NationalNumber"))
                : GetValue(docFields, "NationalNumber");
            var familyNumber =
                document.DocumentType.Equals(
                    PeopleAiDocumentTypes.NationalId,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(familyNumberOverride)
                    ? familyNumberOverride.Trim()
                    : GetValue(docFields, "FamilyNumber");

            var expiryDate =
                document.ReviewedExpiryDate ??
                ParseDate(GetValue(docFields, "ExpiryDate"));
            var issueDate = ParseDate(
                GetValue(docFields, "IssueDate"));
            var issuingAuthority =
                GetValue(docFields, "IssuingAuthority");
            var placeOfIssue =
                GetValue(docFields, "PlaceOfIssue");
            var countryCode =
                GetValue(docFields, "IssuingCountry") ??
                GetValue(docFields, "Nationality");

            var employeeDocumentType =
                MapEmployeeDocumentType(document.DocumentType);

            var employeeDocumentId = await HrmsDatabase.ScalarAsync<long>(
                db,
                """
IF EXISTS
(
    SELECT 1
    FROM dbo.EmployeeDocuments
    WHERE EmployeeId = @EmployeeId
      AND StoredPath = @StoredPath
)
BEGIN
    SELECT TOP 1 Id
    FROM dbo.EmployeeDocuments
    WHERE EmployeeId = @EmployeeId
      AND StoredPath = @StoredPath
    ORDER BY Id;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeDocuments
        (EmployeeId, DocumentType, FileName, StoredPath,
         ExpiryDate, Notes, UploadedBy)
    OUTPUT INSERTED.Id
    VALUES
        (@EmployeeId, @EmployeeDocumentType, @FileName, @StoredPath,
         @ExpiryDate, @Notes, @UploadedBy);
END;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(
                        command, "@EmployeeDocumentType", employeeDocumentType);
                    HrmsDatabase.AddParameter(
                        command, "@FileName", promoted.OriginalFileName);
                    HrmsDatabase.AddParameter(
                        command, "@StoredPath", promoted.StoredPath);
                    HrmsDatabase.AddParameter(
                        command, "@ExpiryDate",
                        (object?)expiryDate ?? DBNull.Value);
                    HrmsDatabase.AddParameter(
                        command, "@Notes",
                        $"Smart Onboarding Session #{sessionId}");
                    HrmsDatabase.AddParameter(
                        command, "@UploadedBy",
                        string.IsNullOrWhiteSpace(uploadedBy)
                            ? "HR"
                            : uploadedBy.Trim());
                });

            long identityId = 0;
            if (IsIdentityDocument(document.DocumentType))
            {
                identityId = await HrmsDatabase.ScalarAsync<long>(
                    db,
                    """
IF EXISTS
(
    SELECT 1
    FROM dbo.EmployeeIdentityDocuments
    WHERE SourceOnboardingDocumentId = @SourceDocumentId
)
BEGIN
    SELECT TOP 1 Id
    FROM dbo.EmployeeIdentityDocuments
    WHERE SourceOnboardingDocumentId = @SourceDocumentId
    ORDER BY Id;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeIdentityDocuments
        (CompanyId, EmployeeId, DocumentType, CountryCode,
         DocumentNumber, NormalizedDocumentNumber,
         NationalNumber, FamilyNumber,
         IssueDate, ExpiryDate, IssuingAuthority, PlaceOfIssue,
         ProtectedFileAssetId, ExtractionStatus,
         VerificationStatus, OriginalVerificationStatus,
         OriginalVerifiedBySystemUserId, OriginalVerifiedAt,
         IsCurrent, SourceOnboardingDocumentId)
    OUTPUT INSERTED.Id
    VALUES
        (@CompanyId, @EmployeeId, @DocumentType, @CountryCode,
         @DocumentNumber, @NormalizedDocumentNumber,
         @NationalNumber, @FamilyNumber,
         @IssueDate, @ExpiryDate, @IssuingAuthority, @PlaceOfIssue,
         @AssetId, 'Processed',
         'HumanReviewed', @OriginalVerificationStatus,
         @OriginalVerifiedBySystemUserId, @OriginalVerifiedAt,
         1, @SourceDocumentId);
END;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(
                            command, "@CompanyId", companyId);
                        HrmsDatabase.AddParameter(
                            command, "@EmployeeId", employeeId);
                        HrmsDatabase.AddParameter(
                            command, "@DocumentType", document.DocumentType);
                        HrmsDatabase.AddParameter(
                            command, "@CountryCode",
                            (object?)countryCode ?? DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@DocumentNumber",
                            string.IsNullOrWhiteSpace(documentNumber)
                                ? DBNull.Value
                                : documentNumber.Trim());
                        HrmsDatabase.AddParameter(
                            command, "@NormalizedDocumentNumber",
                            string.IsNullOrWhiteSpace(normalizedNumber)
                                ? DBNull.Value
                                : normalizedNumber);
                        HrmsDatabase.AddParameter(
                            command, "@NationalNumber",
                            string.IsNullOrWhiteSpace(nationalNumber)
                                ? DBNull.Value
                                : nationalNumber);
                        HrmsDatabase.AddParameter(
                            command, "@FamilyNumber",
                            string.IsNullOrWhiteSpace(familyNumber)
                                ? DBNull.Value
                                : familyNumber);
                        HrmsDatabase.AddParameter(
                            command, "@IssueDate",
                            (object?)issueDate ?? DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@ExpiryDate",
                            (object?)expiryDate ?? DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@IssuingAuthority",
                            (object?)issuingAuthority ?? DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@PlaceOfIssue",
                            (object?)placeOfIssue ?? DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@AssetId", document.AssetId);
                        HrmsDatabase.AddParameter(
                            command, "@OriginalVerificationStatus",
                            string.IsNullOrWhiteSpace(document.OriginalVerificationStatus)
                                ? "NotSeen"
                                : document.OriginalVerificationStatus);
                        HrmsDatabase.AddParameter(
                            command, "@OriginalVerifiedBySystemUserId",
                            (object?)document.OriginalVerifiedBySystemUserId ??
                            DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@OriginalVerifiedAt",
                            (object?)document.OriginalVerifiedAt ??
                            DBNull.Value);
                        HrmsDatabase.AddParameter(
                            command, "@SourceDocumentId", document.DocumentId);
                    });
            }

            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE dbo.ProtectedFileAssets
SET StorageKey = @StorageKey,
    OwnerType = @OwnerType,
    OwnerId = @OwnerId
WHERE Id = @AssetId
  AND CompanyId = @CompanyId;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command, "@StorageKey", promoted.NewStorageKey);
                    HrmsDatabase.AddParameter(
                        command, "@OwnerType",
                        identityId > 0
                            ? "EmployeeIdentityDocument"
                            : "EmployeeDocument");
                    HrmsDatabase.AddParameter(
                        command, "@OwnerId",
                        identityId > 0
                            ? identityId
                            : employeeDocumentId);
                    HrmsDatabase.AddParameter(
                        command, "@AssetId", document.AssetId);
                    HrmsDatabase.AddParameter(
                        command, "@CompanyId", companyId);
                });
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Completed',
    CreatedEmployeeId = @EmployeeId,
    CompletedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND CompanyId = @CompanyId
  AND Status = 'Completing'
  AND CreatedEmployeeId IS NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", companyId);
            });

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.PeopleAiAuditLogs
    (CompanyId, SystemUserId, EmployeeId, SessionId,
     Feature, Operation, Success, CreatedAt)
VALUES
    (@CompanyId, @SystemUserId, @EmployeeId, @SessionId,
     'SmartOnboarding', 'EmployeeCreated', 1, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(
                    command, "@SystemUserId",
                    (object?)reviewerSystemUserId ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
            });
    }

    public static async Task<bool> HasResolvedIssueAsync(
        ApplicationDbContext db,
        long sessionId,
        string ruleCode)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(*)
FROM dbo.OnboardingValidationIssues
WHERE SessionId = @SessionId
  AND RuleCode = @RuleCode
  AND Status = 'Resolved';
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@RuleCode", ruleCode);
            });

        return count > 0;
    }

    private static string MapEmployeeDocumentType(string type) =>
        type switch
        {
            PeopleAiDocumentTypes.NationalId => "ID",
            PeopleAiDocumentTypes.Passport => "Passport",
            PeopleAiDocumentTypes.Residence => "Visa",
            PeopleAiDocumentTypes.Visa => "Visa",
            PeopleAiDocumentTypes.Contract => "Contract",
            PeopleAiDocumentTypes.Cv => "Other",
            _ => "Other"
        };

    private static bool IsIdentityDocument(string type) =>
        type.Equals(PeopleAiDocumentTypes.NationalId,
                StringComparison.OrdinalIgnoreCase) ||
        type.Equals(PeopleAiDocumentTypes.Passport,
                StringComparison.OrdinalIgnoreCase) ||
        type.Equals(PeopleAiDocumentTypes.Residence,
                StringComparison.OrdinalIgnoreCase) ||
        type.Equals(PeopleAiDocumentTypes.Visa,
                StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDocumentNumber(
        string documentType,
        IReadOnlyDictionary<string, string> fields,
        string? nationalIdOverride,
        string? passportOverride)
    {
        if (documentType.Equals(
                PeopleAiDocumentTypes.NationalId,
                StringComparison.OrdinalIgnoreCase))
        {
            return GetValue(fields, "DocumentNumber") ??
                   (!string.IsNullOrWhiteSpace(nationalIdOverride)
                       ? nationalIdOverride.Trim()
                       : GetValue(fields, "NationalNumber"));
        }

        if (documentType.Equals(
                PeopleAiDocumentTypes.Passport,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(passportOverride))
        {
            return passportOverride.Trim();
        }

        return GetValue(fields, "DocumentNumber") ??
               GetValue(fields, "NationalNumber");
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, string> fields,
        string key) =>
        fields.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var parsed)
            ? parsed
            : null;
}
