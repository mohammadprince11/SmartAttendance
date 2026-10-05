using System.Text.Json;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.PeopleAi;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleAiExtractionStore
{
    public sealed record ProcessingInput(
        long DocumentId,
        long SessionId,
        int CompanyId,
        int? CreatedBySystemUserId,
        long ProtectedFileAssetId,
        string StorageKey,
        string Sha256,
        string? DeclaredDocumentType);

    public sealed record DocumentClassification(
        string? DetectedDocumentType,
        decimal? Confidence,
        string? DetectionMethod);

    public static async Task<long> CreateManualReviewRunAsync(
        ApplicationDbContext db,
        long documentId,
        long sessionId,
        int companyId,
        string? declaredDocumentType)
    {
        var documentType = string.IsNullOrWhiteSpace(declaredDocumentType)
            ? PeopleAiDocumentTypes.Unknown
            : declaredDocumentType.Trim();

        var runId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
DECLARE @ExistingRunId bigint =
(
    SELECT TOP (1) r.Id
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    JOIN dbo.EmployeeOnboardingSessions s
      ON s.Id = d.SessionId
    WHERE d.Id = @DocumentId
      AND d.SessionId = @SessionId
      AND s.CompanyId = @CompanyId
      AND r.Provider = 'ManualReview'
      AND r.Model = 'StorageOnly'
    ORDER BY r.Id DESC
);

IF @ExistingRunId IS NOT NULL
BEGIN
    SELECT @ExistingRunId;
END
ELSE
BEGIN
    INSERT INTO dbo.DocumentExtractionRuns
        (OnboardingDocumentId, Provider, Model,
         ExtractorVersion, SchemaVersion, Status,
         StartedAt, CompletedAt)
    OUTPUT INSERTED.Id
    SELECT d.Id, 'ManualReview', 'StorageOnly',
           'manual-review-v1', 'people-ai-extraction-v1',
           'Completed', SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM dbo.OnboardingDocuments d
    JOIN dbo.EmployeeOnboardingSessions s
      ON s.Id = d.SessionId
    WHERE d.Id = @DocumentId
      AND d.SessionId = @SessionId
      AND s.CompanyId = @CompanyId;
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command, "@DocumentId", documentId);
                HrmsDatabase.AddParameter(
                    command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", companyId);
            });

        if (runId <= 0)
        {
            return 0;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.DocumentExtractedFields
    (ExtractionRunId, FieldKey, RawValue, NormalizedValue,
     ProviderConfidence, ValidationStatus, ExtractionMethod,
     ReviewStatus)
SELECT @RunId, fp.FieldKey, NULL, NULL,
       NULL, 'Pending', 'MANUAL', 'Pending'
FROM dbo.CompanyPeopleAiFieldPolicies fp
WHERE fp.CompanyId = @CompanyId
  AND fp.DocumentType = @DocumentType
  AND fp.IsActive = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.DocumentExtractedFields existing
      WHERE existing.ExtractionRunId = @RunId
        AND existing.FieldKey = fp.FieldKey
  );
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RunId", runId);
                HrmsDatabase.AddParameter(
                    command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(
                    command, "@DocumentType", documentType);
            });

        return runId;
    }

    public static async Task<ProcessingInput?> GetProcessingInputAsync(
        ApplicationDbContext db,
        long documentId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT d.Id AS DocumentId, d.SessionId, s.CompanyId,
       s.CreatedBySystemUserId, d.ProtectedFileAssetId,
       a.StorageKey, ISNULL(a.Sha256, '') AS Sha256,
       d.DeclaredDocumentType
FROM dbo.OnboardingDocuments d
JOIN dbo.EmployeeOnboardingSessions s ON s.Id = d.SessionId
JOIN dbo.ProtectedFileAssets a ON a.Id = d.ProtectedFileAssetId
WHERE d.Id = @DocumentId
  AND a.DeletedAt IS NULL
  AND s.Status NOT IN ('Completed', 'Cancelled');
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@DocumentId",
                documentId),
            reader => new ProcessingInput(
                HrmsDatabase.GetLong(reader, "DocumentId"),
                HrmsDatabase.GetLong(reader, "SessionId"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetNullableInt(
                    reader,
                    "CreatedBySystemUserId"),
                HrmsDatabase.GetLong(
                    reader,
                    "ProtectedFileAssetId"),
                HrmsDatabase.GetString(reader, "StorageKey"),
                HrmsDatabase.GetString(reader, "Sha256"),
                NullIfEmpty(HrmsDatabase.GetString(
                    reader,
                    "DeclaredDocumentType"))));

        return rows.FirstOrDefault();
    }

    public static async Task<long> StartRunAsync(
        ApplicationDbContext db,
        ProcessingInput input,
        string provider,
        string model,
        string extractorVersion,
        string schemaVersion)
    {
        var runId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
INSERT INTO dbo.DocumentExtractionRuns
    (OnboardingDocumentId, Provider, Model, ExtractorVersion,
     SchemaVersion, Status, InputHash)
OUTPUT INSERTED.Id
VALUES
    (@DocumentId, @Provider, @Model, @ExtractorVersion,
     @SchemaVersion, 'Processing', @InputHash);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentId",
                    input.DocumentId);
                HrmsDatabase.AddParameter(
                    command,
                    "@Provider",
                    provider);
                HrmsDatabase.AddParameter(
                    command,
                    "@Model",
                    model);
                HrmsDatabase.AddParameter(
                    command,
                    "@ExtractorVersion",
                    extractorVersion);
                HrmsDatabase.AddParameter(
                    command,
                    "@SchemaVersion",
                    schemaVersion);
                HrmsDatabase.AddParameter(
                    command,
                    "@InputHash",
                    string.IsNullOrWhiteSpace(input.Sha256)
                        ? DBNull.Value
                        : input.Sha256);
            });

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingDocuments
SET ProcessingStatus = 'Processing',
    ProcessingErrorCode = NULL
WHERE Id = @DocumentId;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Processing',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentId",
                    input.DocumentId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    input.SessionId);
            });

        return runId;
    }

    public static async Task SaveOcrResultAsync(
        ApplicationDbContext db,
        long runId,
        LocalOcrResponse response)
    {
        if (runId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runId));
        }

        var pageCount = response.Pages?.Count ?? 0;
        foreach (var page in response.Pages ?? [])
        {
            foreach (var line in page.Lines)
            {
                var fieldKey =
                    $"OCR.Page{page.PageIndex + 1}.Line{line.Index + 1:000}";
                var boxJson = line.Box is { Length: > 0 }
                    ? JsonSerializer.Serialize(line.Box)
                    : null;

                await InsertFieldAsync(
                    db,
                    runId,
                    fieldKey,
                    line.Text,
                    line.Text.Trim(),
                    line.Score,
                    "Observed",
                    "OCR",
                    page.PageIndex + 1,
                    boxJson,
                    "Pending");
            }
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.OnboardingDocuments
SET PageCount = @PageCount
WHERE Id = (
    SELECT OnboardingDocumentId
    FROM dbo.DocumentExtractionRuns
    WHERE Id = @RunId
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@PageCount",
                    pageCount);
                HrmsDatabase.AddParameter(
                    command,
                    "@RunId",
                    runId);
            });
    }

    public static Task SaveSemanticFieldAsync(
        ApplicationDbContext db,
        long runId,
        string fieldKey,
        string? rawValue,
        string? normalizedValue,
        double? confidence,
        string validationStatus,
        string extractionMethod,
        string reviewStatus = "Pending") =>
        InsertFieldAsync(
            db,
            runId,
            fieldKey,
            rawValue,
            normalizedValue,
            confidence,
            validationStatus,
            extractionMethod,
            null,
            null,
            reviewStatus);

    public static Task AddValidationIssueAsync(
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
INSERT INTO dbo.OnboardingValidationIssues
    (SessionId, OnboardingDocumentId, RuleCode, Category,
     Severity, FieldKey, Message, Status)
VALUES
    (@SessionId, @DocumentId, @RuleCode, @Category,
     @Severity, @FieldKey, @Message, 'Open');
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentId",
                    (object?)documentId ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@RuleCode",
                    ruleCode);
                HrmsDatabase.AddParameter(
                    command,
                    "@Category",
                    category);
                HrmsDatabase.AddParameter(
                    command,
                    "@Severity",
                    severity);
                HrmsDatabase.AddParameter(
                    command,
                    "@FieldKey",
                    (object?)fieldKey ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@Message",
                    message);
            });

    public static async Task CompleteAsync(
        ApplicationDbContext db,
        ProcessingInput input,
        long runId,
        DocumentClassification classification)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.DocumentExtractionRuns
SET Status = 'Completed',
    CompletedAt = SYSUTCDATETIME(),
    ErrorCode = NULL
WHERE Id = @RunId;

UPDATE dbo.OnboardingDocuments
SET DetectedDocumentType = @DetectedType,
    ClassificationConfidence = @Confidence,
    DetectionMethod = @DetectionMethod,
    ProcessingStatus = 'NeedsReview',
    ProcessingErrorCode = NULL,
    ProcessedAt = SYSUTCDATETIME()
WHERE Id = @DocumentId;

IF @DetectedType IS NOT NULL
   AND @DeclaredType IS NOT NULL
   AND @DetectedType <> @DeclaredType
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.OnboardingValidationIssues
        WHERE SessionId = @SessionId
          AND OnboardingDocumentId = @DocumentId
          AND RuleCode = 'DOCUMENT_TYPE_MISMATCH'
          AND Status = 'Open'
    )
    BEGIN
        INSERT INTO dbo.OnboardingValidationIssues
            (SessionId, OnboardingDocumentId, RuleCode, Category,
             Severity, FieldKey, Message, Status)
        VALUES
            (@SessionId, @DocumentId, 'DOCUMENT_TYPE_MISMATCH',
             'Classification', 'Warning', NULL,
             CONCAT(N'نوع المستند المعلن (', @DeclaredType,
                    N') يختلف عن النوع المكتشف (', @DetectedType,
                    N'). يلزم التحقق البشري قبل الاعتماد.'),
             'Open');
    END;
END
ELSE
BEGIN
    UPDATE dbo.OnboardingValidationIssues
    SET Status = 'Resolved',
        ResolvedAt = SYSUTCDATETIME(),
        Resolution = N'أعيدت المعالجة ولم يعد يوجد تعارض بين النوع المعلن والمكتشف.'
    WHERE SessionId = @SessionId
      AND OnboardingDocumentId = @DocumentId
      AND RuleCode = 'DOCUMENT_TYPE_MISMATCH'
      AND Status = 'Open';
END;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = CASE
        WHEN EXISTS (
            SELECT 1
            FROM dbo.OnboardingDocuments
            WHERE SessionId = @SessionId
              AND ProcessingStatus IN ('Queued', 'Processing'))
        THEN 'Processing'
        ELSE 'NeedsReview'
    END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RunId", runId);
                HrmsDatabase.AddParameter(
                    command,
                    "@DetectedType",
                    (object?)classification.DetectedDocumentType ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@Confidence",
                    (object?)classification.Confidence ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@DetectionMethod",
                    (object?)classification.DetectionMethod ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@DeclaredType",
                    string.IsNullOrWhiteSpace(input.DeclaredDocumentType)
                        ? DBNull.Value
                        : input.DeclaredDocumentType.Trim());
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentId",
                    input.DocumentId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    input.SessionId);
            });
    }

    public static async Task FailAsync(
        ApplicationDbContext db,
        ProcessingInput input,
        long? runId,
        string errorCode,
        bool willRetry)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF @RunId IS NOT NULL
BEGIN
    UPDATE dbo.DocumentExtractionRuns
    SET Status = 'Failed',
        CompletedAt = SYSUTCDATETIME(),
        ErrorCode = @ErrorCode
    WHERE Id = @RunId;
END;

UPDATE dbo.OnboardingDocuments
SET ProcessingStatus = CASE WHEN @WillRetry = 1 THEN 'Queued' ELSE 'Failed' END,
    ProcessingErrorCode = @ErrorCode,
    ProcessedAt = CASE WHEN @WillRetry = 1 THEN NULL ELSE SYSUTCDATETIME() END
WHERE Id = @DocumentId;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = CASE WHEN @WillRetry = 1 THEN 'Queued' ELSE 'NeedsReview' END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@RunId",
                    (object?)runId ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@ErrorCode",
                    errorCode);
                HrmsDatabase.AddParameter(
                    command,
                    "@WillRetry",
                    willRetry);
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentId",
                    input.DocumentId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    input.SessionId);
            });
    }

    public static Task RecordAuditAsync(
        ApplicationDbContext db,
        ProcessingInput input,
        string operation,
        string? provider,
        string? model,
        bool success,
        int durationMs,
        string? errorCode = null) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.PeopleAiAuditLogs
    (CompanyId, SystemUserId, SessionId, Feature, Operation,
     Provider, Model, CorrelationId, Success, DurationMs,
     ErrorCode)
VALUES
    (@CompanyId, @SystemUserId, @SessionId, 'SmartOnboarding',
     @Operation, @Provider, @Model, @CorrelationId, @Success,
     @DurationMs, @ErrorCode);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@CompanyId",
                    input.CompanyId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SystemUserId",
                    (object?)input.CreatedBySystemUserId ??
                    DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    input.SessionId);
                HrmsDatabase.AddParameter(
                    command,
                    "@Operation",
                    operation);
                HrmsDatabase.AddParameter(
                    command,
                    "@Provider",
                    (object?)provider ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@Model",
                    (object?)model ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@CorrelationId",
                    Guid.NewGuid().ToString("N"));
                HrmsDatabase.AddParameter(
                    command,
                    "@Success",
                    success);
                HrmsDatabase.AddParameter(
                    command,
                    "@DurationMs",
                    durationMs);
                HrmsDatabase.AddParameter(
                    command,
                    "@ErrorCode",
                    (object?)errorCode ?? DBNull.Value);
            });

    private static Task InsertFieldAsync(
        ApplicationDbContext db,
        long runId,
        string fieldKey,
        string? rawValue,
        string? normalizedValue,
        double? confidence,
        string validationStatus,
        string extractionMethod,
        int? sourcePage,
        string? sourceBoundingBox,
        string reviewStatus) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.DocumentExtractedFields
    (ExtractionRunId, FieldKey, RawValue, NormalizedValue,
     ProviderConfidence, ValidationStatus, ExtractionMethod,
     SourcePage, SourceBoundingBox, ReviewStatus)
VALUES
    (@RunId, @FieldKey, @RawValue, @NormalizedValue,
     @Confidence, @ValidationStatus, @ExtractionMethod,
     @SourcePage, @SourceBoundingBox, @ReviewStatus);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@RunId",
                    runId);
                HrmsDatabase.AddParameter(
                    command,
                    "@FieldKey",
                    fieldKey);
                HrmsDatabase.AddParameter(
                    command,
                    "@RawValue",
                    (object?)rawValue ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@NormalizedValue",
                    (object?)normalizedValue ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@Confidence",
                    confidence.HasValue
                        ? Convert.ToDecimal(confidence.Value)
                        : DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@ValidationStatus",
                    validationStatus);
                HrmsDatabase.AddParameter(
                    command,
                    "@ExtractionMethod",
                    extractionMethod);
                HrmsDatabase.AddParameter(
                    command,
                    "@SourcePage",
                    (object?)sourcePage ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@SourceBoundingBox",
                    (object?)sourceBoundingBox ?? DBNull.Value);
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewStatus",
                    reviewStatus);
            });

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
