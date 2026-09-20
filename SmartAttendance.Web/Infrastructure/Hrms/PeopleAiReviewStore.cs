using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleAiReviewStore
{
    public const decimal HighConfidenceThreshold = 0.90m;
    public sealed record ReviewField(
        long Id,
        long ExtractionRunId,
        long DocumentId,
        string FieldKey,
        string? RawValue,
        string? NormalizedValue,
        decimal? ProviderConfidence,
        string ValidationStatus,
        string ExtractionMethod,
        int? SourcePage,
        string ReviewStatus,
        string? ReviewedValue,
        int? ReviewedBySystemUserId,
        DateTime? ReviewedAt);

    public sealed record ValidationIssue(
        long Id,
        long SessionId,
        long? DocumentId,
        string RuleCode,
        string Category,
        string Severity,
        string? FieldKey,
        string Message,
        string Status,
        string? Resolution);

    public static async Task<List<ReviewField>> ListLatestFieldsAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        return await HrmsDatabase.QueryAsync(
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
SELECT f.Id, f.ExtractionRunId,
       lr.OnboardingDocumentId AS DocumentId,
       f.FieldKey, f.RawValue, f.NormalizedValue,
       f.ProviderConfidence, f.ValidationStatus,
       f.ExtractionMethod, f.SourcePage,
       f.ReviewStatus, f.ReviewedValue,
       f.ReviewedBySystemUserId, f.ReviewedAt
FROM LatestRuns lr
JOIN dbo.DocumentExtractedFields f
  ON f.ExtractionRunId = lr.Id
WHERE lr.rn = 1
ORDER BY lr.OnboardingDocumentId,
         CASE WHEN f.FieldKey LIKE 'OCR.%' THEN 1 ELSE 0 END,
         f.Id;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId),
            reader => new ReviewField(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetLong(reader, "ExtractionRunId"),
                HrmsDatabase.GetLong(reader, "DocumentId"),
                HrmsDatabase.GetString(reader, "FieldKey"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "RawValue")),
                NullIfEmpty(HrmsDatabase.GetString(reader, "NormalizedValue")),
                HrmsDatabase.GetNullableDecimal(reader, "ProviderConfidence"),
                HrmsDatabase.GetString(reader, "ValidationStatus"),
                HrmsDatabase.GetString(reader, "ExtractionMethod"),
                HrmsDatabase.GetNullableInt(reader, "SourcePage"),
                HrmsDatabase.GetString(reader, "ReviewStatus"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "ReviewedValue")),
                HrmsDatabase.GetNullableInt(reader, "ReviewedBySystemUserId"),
                HrmsDatabase.GetDateTime(reader, "ReviewedAt")));
    }

    public static async Task EnsureConfiguredFieldsAsync(
        ApplicationDbContext db,
        int companyId,
        long sessionId)
    {
        await PeopleAiSettingsStore.EnsureDefaultsAsync(db);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
;WITH LatestRuns AS
(
    SELECT r.Id AS RunId,
           r.OnboardingDocumentId,
           COALESCE(NULLIF(d.DetectedDocumentType, ''),
                    NULLIF(d.DeclaredDocumentType, ''),
                    'Unknown') AS DocumentType,
           ROW_NUMBER() OVER(
               PARTITION BY r.OnboardingDocumentId
               ORDER BY r.Id DESC) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    JOIN dbo.EmployeeOnboardingSessions s
      ON s.Id = d.SessionId
    WHERE d.SessionId = @SessionId
      AND s.CompanyId = @CompanyId
),
CurrentRuns AS
(
    SELECT RunId, OnboardingDocumentId, DocumentType
    FROM LatestRuns
    WHERE rn = 1
),
MissingFields AS
(
    SELECT fp.DocumentType,
           fp.FieldKey,
           MIN(cr.RunId) AS TargetRunId
    FROM dbo.CompanyPeopleAiFieldPolicies fp
    JOIN CurrentRuns cr
      ON cr.DocumentType = fp.DocumentType
    WHERE fp.CompanyId = @CompanyId
      AND fp.IsActive = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM CurrentRuns existingRun
          JOIN dbo.DocumentExtractedFields existingField
            ON existingField.ExtractionRunId = existingRun.RunId
          WHERE existingRun.DocumentType = fp.DocumentType
            AND existingField.FieldKey = fp.FieldKey
      )
    GROUP BY fp.DocumentType, fp.FieldKey
)
INSERT INTO dbo.DocumentExtractedFields
    (ExtractionRunId, FieldKey, RawValue, NormalizedValue,
     ProviderConfidence, ValidationStatus, ExtractionMethod,
     ReviewStatus)
SELECT mf.TargetRunId, mf.FieldKey, NULL, NULL,
       NULL, 'Pending', 'Manual', 'Pending'
FROM MissingFields mf;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
            });
    }

    public static async Task<int> AcceptHighConfidenceConfiguredFieldsAsync(
        ApplicationDbContext db,
        int companyId,
        long sessionId,
        int systemUserId)
    {
        await PeopleAiSettingsStore.EnsureDefaultsAsync(db);

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
;WITH LatestRuns AS
(
    SELECT r.Id AS RunId,
           r.OnboardingDocumentId,
           COALESCE(NULLIF(d.DetectedDocumentType, ''),
                    NULLIF(d.DeclaredDocumentType, ''),
                    'Unknown') AS DocumentType,
           ROW_NUMBER() OVER(
               PARTITION BY r.OnboardingDocumentId
               ORDER BY r.Id DESC) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    JOIN dbo.EmployeeOnboardingSessions s
      ON s.Id = d.SessionId
    WHERE d.SessionId = @SessionId
      AND s.CompanyId = @CompanyId
)
UPDATE f
SET ReviewStatus = 'Accepted',
    ReviewedValue = COALESCE(
        NULLIF(LTRIM(RTRIM(f.NormalizedValue)), ''),
        NULLIF(LTRIM(RTRIM(f.RawValue)), '')),
    ReviewedBySystemUserId = @ReviewerId,
    ReviewedAt = SYSUTCDATETIME()
FROM LatestRuns lr
JOIN dbo.DocumentExtractedFields f
  ON f.ExtractionRunId = lr.RunId
JOIN dbo.CompanyPeopleAiFieldPolicies fp
  ON fp.CompanyId = @CompanyId
 AND fp.DocumentType = lr.DocumentType
 AND fp.FieldKey = f.FieldKey
 AND fp.IsActive = 1
 AND fp.AllowBulkApprove = 1
WHERE lr.rn = 1
  AND f.ReviewStatus = 'Pending'
  AND f.ValidationStatus <> 'Invalid'
  AND f.ProviderConfidence >= @HighConfidenceThreshold
  AND COALESCE(
        NULLIF(LTRIM(RTRIM(f.NormalizedValue)), ''),
        NULLIF(LTRIM(RTRIM(f.RawValue)), '')) IS NOT NULL;

SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@ReviewerId", systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@HighConfidenceThreshold",
                    HighConfidenceThreshold);
            });
    }

    public static async Task<List<ValidationIssue>> ListIssuesAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, SessionId, OnboardingDocumentId,
       RuleCode, Category, Severity, FieldKey,
       Message, Status, Resolution
FROM dbo.OnboardingValidationIssues
WHERE SessionId = @SessionId
ORDER BY
    CASE Severity
        WHEN 'Blocking' THEN 0
        WHEN 'Warning' THEN 1
        ELSE 2
    END,
    Id;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId),
            reader => new ValidationIssue(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetLong(reader, "SessionId"),
                HrmsDatabase.GetNullableLong(reader, "OnboardingDocumentId"),
                HrmsDatabase.GetString(reader, "RuleCode"),
                HrmsDatabase.GetString(reader, "Category"),
                HrmsDatabase.GetString(reader, "Severity"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "FieldKey")),
                HrmsDatabase.GetString(reader, "Message"),
                HrmsDatabase.GetString(reader, "Status"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "Resolution"))));
    }

    public static Task SetReviewedExpiryDateAsync(
        ApplicationDbContext db,
        long sessionId,
        long documentId,
        DateOnly? expiryDate) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingDocuments
SET ReviewedExpiryDate = @ExpiryDate
WHERE Id = @DocumentId
  AND SessionId = @SessionId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@ExpiryDate",
                    (object?)expiryDate ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@DocumentId", documentId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
            });

    public static Task SetOriginalVerificationAsync(
        ApplicationDbContext db,
        long sessionId,
        long documentId,
        int systemUserId,
        string action)
    {
        var status = action switch
        {
            "Seen" => "Seen",
            "Verified" => "Verified",
            "Rejected" => "Rejected",
            _ => throw new InvalidOperationException(
                "Unsupported original verification action.")
        };

        return HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingDocuments
SET OriginalVerificationStatus = @Status,
    OriginalVerifiedBySystemUserId = @ReviewerId,
    OriginalVerifiedAt = SYSUTCDATETIME()
WHERE Id = @DocumentId
  AND SessionId = @SessionId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@ReviewerId", systemUserId);
                HrmsDatabase.AddParameter(command, "@DocumentId", documentId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
            });
    }

    public static Task ReviewFieldAsync(
        ApplicationDbContext db,
        long sessionId,
        long fieldId,
        int systemUserId,
        string action,
        string? reviewedValue)
    {
        var status = action switch
        {
            "Accept" => "Accepted",
            "Modify" => "Modified",
            "Reject" => "Rejected",
            _ => throw new InvalidOperationException(
                "Unsupported review action.")
        };

        var value = status == "Rejected"
            ? null
            : (reviewedValue ?? string.Empty).Trim();

        return HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE f
SET ReviewStatus = @ReviewStatus,
    ReviewedValue = @ReviewedValue,
    ReviewedBySystemUserId = @ReviewerId,
    ReviewedAt = SYSUTCDATETIME()
FROM dbo.DocumentExtractedFields f
JOIN dbo.DocumentExtractionRuns r
  ON r.Id = f.ExtractionRunId
JOIN dbo.OnboardingDocuments d
  ON d.Id = r.OnboardingDocumentId
WHERE f.Id = @FieldId
  AND d.SessionId = @SessionId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewStatus",
                    status);
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewedValue",
                    (object?)value ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewerId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@FieldId",
                    fieldId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
            });
    }

    public static Task EnsureIssueAsync(
        ApplicationDbContext db,
        long sessionId,
        long? documentId,
        string ruleCode,
        string category,
        string severity,
        string? fieldKey,
        string message) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OnboardingValidationIssues
    WHERE SessionId = @SessionId
      AND RuleCode = @RuleCode
      AND ISNULL(OnboardingDocumentId, 0) = ISNULL(@DocumentId, 0)
)
BEGIN
    INSERT INTO dbo.OnboardingValidationIssues
        (SessionId, OnboardingDocumentId, RuleCode, Category,
         Severity, FieldKey, Message, Status)
    VALUES
        (@SessionId, @DocumentId, @RuleCode, @Category,
         @Severity, @FieldKey, @Message, 'Open');
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@DocumentId",
                    (object?)documentId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@RuleCode", ruleCode);
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@Severity", severity);
                HrmsDatabase.AddParameter(
                    command, "@FieldKey",
                    (object?)fieldKey ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Message", message);
            });

    public static Task UpsertDynamicIssueAsync(
        ApplicationDbContext db,
        long sessionId,
        string ruleCode,
        string category,
        string severity,
        string? fieldKey,
        string message) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
IF EXISTS
(
    SELECT 1
    FROM dbo.OnboardingValidationIssues
    WHERE SessionId = @SessionId
      AND RuleCode = @RuleCode
      AND OnboardingDocumentId IS NULL
)
BEGIN
    UPDATE dbo.OnboardingValidationIssues
    SET Category = @Category,
        Severity = @Severity,
        FieldKey = @FieldKey,
        Message = @Message
    WHERE SessionId = @SessionId
      AND RuleCode = @RuleCode
      AND OnboardingDocumentId IS NULL
      AND Status = 'Open';
END
ELSE
BEGIN
    INSERT INTO dbo.OnboardingValidationIssues
        (SessionId, OnboardingDocumentId, RuleCode, Category,
         Severity, FieldKey, Message, Status)
    VALUES
        (@SessionId, NULL, @RuleCode, @Category,
         @Severity, @FieldKey, @Message, 'Open');
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@RuleCode", ruleCode);
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@Severity", severity);
                HrmsDatabase.AddParameter(
                    command,
                    "@FieldKey",
                    (object?)fieldKey ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Message", message);
            });

    public static Task ResolveRuleAutomaticallyAsync(
        ApplicationDbContext db,
        long sessionId,
        string ruleCode,
        string resolution) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingValidationIssues
SET Status = 'Resolved',
    ResolvedAt = SYSUTCDATETIME(),
    Resolution = @Resolution
WHERE SessionId = @SessionId
  AND RuleCode = @RuleCode
  AND Status = 'Open';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@RuleCode", ruleCode);
                HrmsDatabase.AddParameter(command, "@Resolution", resolution);
            });

    public static Task ResolveIssueAsync(
        ApplicationDbContext db,
        long sessionId,
        long issueId,
        int systemUserId,
        string resolution)
    {
        return HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingValidationIssues
SET Status = 'Resolved',
    ResolvedBySystemUserId = @ReviewerId,
    ResolvedAt = SYSUTCDATETIME(),
    Resolution = @Resolution
WHERE Id = @IssueId
  AND SessionId = @SessionId
  AND Status = 'Open';
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewerId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@Resolution",
                    resolution.Trim());
                HrmsDatabase.AddParameter(
                    command,
                    "@IssueId",
                    issueId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
            });
    }

    public static async Task<bool> CanMarkReadyAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        var blockers = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(*)
FROM dbo.OnboardingValidationIssues
WHERE SessionId = @SessionId
  AND Status = 'Open'
  AND Severity = 'Blocking';
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId));

        if (blockers > 0)
        {
            return false;
        }

        var unfinishedDocuments = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(*)
FROM dbo.OnboardingDocuments
WHERE SessionId = @SessionId
  AND ProcessingStatus IN ('Queued', 'Processing', 'Failed');
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId));

        if (unfinishedDocuments > 0)
        {
            return false;
        }

        var pendingRequired = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
WITH LatestRuns AS
(
    SELECT r.Id AS RunId,
           s.CompanyId,
           COALESCE(NULLIF(d.DetectedDocumentType, ''),
                    NULLIF(d.DeclaredDocumentType, ''),
                    'Unknown') AS DocumentType,
           ROW_NUMBER() OVER(
               PARTITION BY r.OnboardingDocumentId
               ORDER BY r.Id DESC) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    JOIN dbo.EmployeeOnboardingSessions s
      ON s.Id = d.SessionId
    WHERE d.SessionId = @SessionId
),
CurrentRuns AS
(
    SELECT RunId, CompanyId, DocumentType
    FROM LatestRuns
    WHERE rn = 1
),
RequiredPolicies AS
(
    SELECT DISTINCT fp.CompanyId, fp.DocumentType, fp.FieldKey
    FROM dbo.CompanyPeopleAiFieldPolicies fp
    JOIN CurrentRuns cr
      ON cr.CompanyId = fp.CompanyId
     AND cr.DocumentType = fp.DocumentType
    WHERE fp.IsActive = 1
      AND fp.Requirement = 'Required'
)
SELECT COUNT(*)
FROM RequiredPolicies rp
WHERE NOT EXISTS
(
    SELECT 1
    FROM CurrentRuns cr
    JOIN dbo.DocumentExtractedFields f
      ON f.ExtractionRunId = cr.RunId
    WHERE cr.CompanyId = rp.CompanyId
      AND cr.DocumentType = rp.DocumentType
      AND f.FieldKey = rp.FieldKey
      AND f.ReviewStatus IN ('Accepted', 'Modified')
      AND COALESCE(
          NULLIF(LTRIM(RTRIM(f.ReviewedValue)), ''),
          NULLIF(LTRIM(RTRIM(f.NormalizedValue)), ''),
          NULLIF(LTRIM(RTRIM(f.RawValue)), '')) IS NOT NULL
);
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId));

        return pendingRequired == 0;
    }

    public static async Task ReopenForReviewAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'NeedsReview',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status = 'Ready'
  AND CreatedEmployeeId IS NULL;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId));
    }

    public static async Task<bool> MarkReadyAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        if (!await CanMarkReadyAsync(db, sessionId))
        {
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Ready',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status IN ('NeedsReview', 'Processing', 'Queued');
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId));

        return true;
    }

    public static async Task<Dictionary<string, string>> AcceptedValuesAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
WITH LatestRuns AS
(
    SELECT r.Id,
           ROW_NUMBER() OVER(
               PARTITION BY r.OnboardingDocumentId
               ORDER BY r.Id DESC) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
)
SELECT f.FieldKey,
       COALESCE(NULLIF(f.ReviewedValue, ''),
                NULLIF(f.NormalizedValue, ''),
                f.RawValue) AS FinalValue,
       f.Id
FROM LatestRuns lr
JOIN dbo.DocumentExtractedFields f
  ON f.ExtractionRunId = lr.Id
WHERE lr.rn = 1
  AND f.ReviewStatus IN ('Accepted', 'Modified')
  AND f.FieldKey NOT LIKE 'OCR.%'
  AND f.FieldKey NOT LIKE 'MRZ.Check.%'
ORDER BY f.Id DESC;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@SessionId",
                sessionId),
            reader => new KeyValuePair<string, string>(
                HrmsDatabase.GetString(reader, "FieldKey"),
                HrmsDatabase.GetString(reader, "FinalValue")));

        return rows
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
