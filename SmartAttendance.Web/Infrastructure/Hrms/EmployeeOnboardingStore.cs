using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeOnboardingStore
{
    public sealed record SessionRow(
        long Id,
        int CompanyId,
        int? CreatedBySystemUserId,
        string Status,
        int? CreatedEmployeeId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? ExpiresAt);

    public sealed record DocumentRow(
        long Id,
        long SessionId,
        long ProtectedFileAssetId,
        string OriginalFileName,
        long SizeBytes,
        string Sha256,
        string? DeclaredDocumentType,
        string? DetectedDocumentType,
        decimal? ClassificationConfidence,
        string? DetectionMethod,
        string ProcessingStatus,
        int? PageCount,
        string? ProcessingErrorCode,
        string OriginalVerificationStatus,
        int? OriginalVerifiedBySystemUserId,
        DateTime? OriginalVerifiedAt,
        DateOnly? ReviewedExpiryDate,
        DateTime UploadedAt,
        DateTime? ProcessedAt,
        string? JobStatus,
        int JobAttemptCount,
        int JobMaxAttempts,
        DateTime? JobLockedAt,
        DateTime? JobNextAttemptAt,
        int JobsAhead,
        int ActiveQueueCount,
        int SuccessfulDurationSamples,
        long? AverageSuccessfulDurationMs);

    public sealed record JobRow(
        long Id,
        int CompanyId,
        long? SessionId,
        long? OnboardingDocumentId,
        string JobType,
        string IdempotencyKey,
        string Status,
        int AttemptCount,
        int MaxAttempts);

    public static async Task<long> CreateSessionAsync(
        ApplicationDbContext db,
        int companyId,
        int? createdBySystemUserId,
        TimeSpan? lifetime = null)
    {
        await PeopleAiSettingsStore.EnsureDefaultsAsync(db);
        var expiresAt = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(7));

        return await HrmsDatabase.ScalarAsync<long>(
            db,
            """
INSERT INTO dbo.EmployeeOnboardingSessions
    (CompanyId, CreatedBySystemUserId, Status, ExpiresAt)
OUTPUT INSERTED.Id
SELECT @CompanyId, @CreatedBy, 'Draft', @ExpiresAt
WHERE EXISTS (
    SELECT 1 FROM dbo.Companies
    WHERE Id = @CompanyId AND IsActive = 1 AND ISNULL(IsDeleted, 0) = 0)
  AND EXISTS (
    SELECT 1 FROM dbo.CompanyPeopleAiSettings
    WHERE CompanyId = @CompanyId AND IsEnabled = 1);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(
                    command, "@CreatedBy", (object?)createdBySystemUserId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@ExpiresAt", expiresAt);
            });
    }

    public static async Task<SessionRow?> GetSessionAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, CompanyId, CreatedBySystemUserId, Status, CreatedEmployeeId,
       CreatedAt, UpdatedAt, ExpiresAt
FROM dbo.EmployeeOnboardingSessions
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", sessionId),
            reader => new SessionRow(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetNullableInt(reader, "CreatedBySystemUserId"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetNullableInt(reader, "CreatedEmployeeId"),
                HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "UpdatedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "ExpiresAt")));

        return rows.FirstOrDefault();
    }

    public static Task<long> AddDocumentAndQueueAsync(
        ApplicationDbContext db,
        long sessionId,
        long protectedFileAssetId,
        string? declaredDocumentType) =>
        AddDocumentAsync(
            db,
            sessionId,
            protectedFileAssetId,
            declaredDocumentType,
            queueAutomaticExtraction: true);

    public static async Task<long> AddDocumentAsync(
        ApplicationDbContext db,
        long sessionId,
        long protectedFileAssetId,
        string? declaredDocumentType,
        bool queueAutomaticExtraction)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var documentId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
INSERT INTO dbo.OnboardingDocuments
    (SessionId, ProtectedFileAssetId, DeclaredDocumentType, ProcessingStatus)
OUTPUT INSERTED.Id
SELECT s.Id, a.Id, @DeclaredType, @ProcessingStatus
FROM dbo.EmployeeOnboardingSessions s
JOIN dbo.ProtectedFileAssets a
  ON a.Id = @AssetId
 AND a.CompanyId = s.CompanyId
 AND a.OwnerType = 'OnboardingSession'
 AND a.OwnerId = s.Id
 AND a.DeletedAt IS NULL
WHERE s.Id = @SessionId
  AND s.Status NOT IN ('Completed', 'Cancelled')
  AND (s.ExpiresAt IS NULL OR s.ExpiresAt > SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@AssetId", protectedFileAssetId);
                HrmsDatabase.AddParameter(
                    command, "@DeclaredType",
                    string.IsNullOrWhiteSpace(declaredDocumentType)
                        ? DBNull.Value
                        : declaredDocumentType.Trim());
                HrmsDatabase.AddParameter(
                    command,
                    "@ProcessingStatus",
                    queueAutomaticExtraction ? "Queued" : "Stored");
            });

        if (documentId <= 0)
        {
            await transaction.RollbackAsync();
            return 0;
        }

        var session = await GetSessionAsync(db, sessionId);
        if (session is null)
        {
            await transaction.RollbackAsync();
            return 0;
        }

        if (queueAutomaticExtraction)
        {
            var idempotencyKey =
                $"onboarding-document:{documentId}:extract-v1";

            await HrmsDatabase.ExecuteAsync(
                db,
                """
IF NOT EXISTS (
    SELECT 1 FROM dbo.PeopleAiJobs WHERE IdempotencyKey = @Key)
BEGIN
    INSERT INTO dbo.PeopleAiJobs
        (CompanyId, SessionId, OnboardingDocumentId, JobType,
         IdempotencyKey, Status, NextAttemptAt)
    VALUES
        (@CompanyId, @SessionId, @DocumentId, 'ProcessOnboardingDocument',
         @Key, 'Queued', SYSUTCDATETIME());
END;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Queued', UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId AND Status IN ('Draft', 'Uploading', 'NeedsReview');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command, "@CompanyId", session.CompanyId);
                    HrmsDatabase.AddParameter(
                        command, "@SessionId", sessionId);
                    HrmsDatabase.AddParameter(
                        command, "@DocumentId", documentId);
                    HrmsDatabase.AddParameter(
                        command, "@Key", idempotencyKey);
                });
        }
        else
        {
            var manualRunId =
                await PeopleAiExtractionStore.CreateManualReviewRunAsync(
                    db,
                    documentId,
                    sessionId,
                    session.CompanyId,
                    declaredDocumentType);

            if (manualRunId <= 0)
            {
                await transaction.RollbackAsync();
                return 0;
            }

            var unsupportedSeverity =
                SmartAttendance.Web.Infrastructure.PeopleAi
                    .DocumentProcessingContract
                    .HasStructuredExtractorDefinition(declaredDocumentType)
                    ? "Blocking"
                    : "Warning";

            await HrmsDatabase.ExecuteAsync(
                db,
                """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OnboardingValidationIssues
    WHERE SessionId = @SessionId
      AND OnboardingDocumentId = @DocumentId
      AND RuleCode = 'AUTOMATIC_EXTRACTION_UNSUPPORTED'
      AND Status = 'Open'
)
BEGIN
    INSERT INTO dbo.OnboardingValidationIssues
        (SessionId, OnboardingDocumentId, RuleCode, Category,
         Severity, FieldKey, Message, Status)
    VALUES
        (@SessionId, @DocumentId,
         'AUTOMATIC_EXTRACTION_UNSUPPORTED',
         'Processing', @Severity, NULL,
         N'تم حفظ المستند للمراجعة، لكن الاستخراج التلقائي غير مدعوم لهذا التنسيق حالياً.',
         'Open');
END;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.OnboardingDocuments
            WHERE SessionId = @SessionId
              AND ProcessingStatus IN ('Queued', 'Processing')
        )
        THEN Status
        ELSE 'NeedsReview'
    END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command, "@SessionId", sessionId);
                    HrmsDatabase.AddParameter(
                        command, "@Severity", unsupportedSeverity);
                    HrmsDatabase.AddParameter(
                        command, "@DocumentId", documentId);
                });
        }

        await transaction.CommitAsync();
        return documentId;
    }

    public static Task MarkDocumentStoredWithoutExtractionAsync(
        ApplicationDbContext db,
        long jobId,
        long documentId,
        long sessionId,
        string unsupportedSeverity) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PeopleAiJobs
SET Status = 'Completed',
    LockedAt = NULL,
    LockedBy = NULL,
    LastErrorCode = NULL,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @JobId
  AND Status = 'Processing';

UPDATE dbo.OnboardingDocuments
SET ProcessingStatus = 'Stored',
    ProcessingErrorCode = NULL,
    ProcessedAt = NULL
WHERE Id = @DocumentId
  AND SessionId = @SessionId;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OnboardingValidationIssues
    WHERE SessionId = @SessionId
      AND OnboardingDocumentId = @DocumentId
      AND RuleCode = 'AUTOMATIC_EXTRACTION_UNSUPPORTED'
      AND Status = 'Open'
)
BEGIN
    INSERT INTO dbo.OnboardingValidationIssues
        (SessionId, OnboardingDocumentId, RuleCode, Category,
         Severity, FieldKey, Message, Status)
    VALUES
        (@SessionId, @DocumentId,
         'AUTOMATIC_EXTRACTION_UNSUPPORTED',
         'Processing', @Severity, NULL,
         N'تم حفظ المستند للمراجعة، لكن الاستخراج التلقائي غير مدعوم لهذا التنسيق حالياً.',
         'Open');
END;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.OnboardingDocuments
            WHERE SessionId = @SessionId
              AND ProcessingStatus IN ('Queued', 'Processing')
        )
        THEN 'Processing'
        ELSE 'NeedsReview'
    END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@JobId", jobId);
                HrmsDatabase.AddParameter(command, "@DocumentId", documentId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(
                    command, "@Severity", unsupportedSeverity);
            });

    public static async Task<List<DocumentRow>> ListDocumentsAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT d.Id, d.SessionId, d.ProtectedFileAssetId,
       a.OriginalFileName, a.SizeBytes, ISNULL(a.Sha256, '') AS Sha256,
       d.DeclaredDocumentType, d.DetectedDocumentType,
       d.ClassificationConfidence, d.DetectionMethod,
       d.ProcessingStatus, d.PageCount, d.ProcessingErrorCode,
       d.OriginalVerificationStatus,
       d.OriginalVerifiedBySystemUserId,
       d.OriginalVerifiedAt,
       d.ReviewedExpiryDate,
       d.UploadedAt, d.ProcessedAt,
       j.Status AS JobStatus,
       ISNULL(j.AttemptCount, 0) AS JobAttemptCount,
       ISNULL(j.MaxAttempts, 0) AS JobMaxAttempts,
       j.LockedAt AS JobLockedAt,
       j.NextAttemptAt AS JobNextAttemptAt,
       ISNULL(queueInfo.JobsAhead, 0) AS JobsAhead,
       ISNULL(activeInfo.ActiveQueueCount, 0) AS ActiveQueueCount,
       ISNULL(durationInfo.SuccessfulDurationSamples, 0) AS SuccessfulDurationSamples,
       durationInfo.AverageSuccessfulDurationMs
FROM dbo.OnboardingDocuments d
JOIN dbo.ProtectedFileAssets a ON a.Id = d.ProtectedFileAssetId
OUTER APPLY
(
    SELECT TOP (1)
        pj.Id, pj.Status, pj.AttemptCount, pj.MaxAttempts,
        pj.LockedAt, pj.NextAttemptAt, pj.CreatedAt
    FROM dbo.PeopleAiJobs pj
    WHERE pj.OnboardingDocumentId = d.Id
    ORDER BY pj.Id DESC
) j
OUTER APPLY
(
    SELECT COUNT(*) AS JobsAhead
    FROM dbo.PeopleAiJobs q
    WHERE j.Id IS NOT NULL
      AND q.Id <> j.Id
      AND q.Status IN ('Processing', 'Queued', 'Retry')
      AND q.AttemptCount < q.MaxAttempts
      AND
      (
          q.Status = 'Processing'
          OR
          (
              (q.NextAttemptAt IS NULL OR q.NextAttemptAt <= SYSUTCDATETIME())
              AND
              (
                  q.CreatedAt < j.CreatedAt
                  OR (q.CreatedAt = j.CreatedAt AND q.Id < j.Id)
              )
          )
      )
) queueInfo
OUTER APPLY
(
    SELECT COUNT(*) AS ActiveQueueCount
    FROM dbo.PeopleAiJobs q
    WHERE q.Status IN ('Processing', 'Queued', 'Retry')
      AND q.AttemptCount < q.MaxAttempts
) activeInfo
OUTER APPLY
(
    SELECT
        COUNT(*) AS SuccessfulDurationSamples,
        CAST(AVG(CAST(sample.DurationMs AS bigint)) AS bigint)
            AS AverageSuccessfulDurationMs
    FROM
    (
        SELECT TOP (10) log.DurationMs
        FROM dbo.PeopleAiAuditLogs log
        WHERE log.Feature = 'SmartOnboarding'
          AND log.Operation = 'DocumentProcessed'
          AND log.Success = 1
          AND log.DurationMs IS NOT NULL
        ORDER BY log.Id DESC
    ) sample
) durationInfo
WHERE d.SessionId = @SessionId
ORDER BY d.UploadedAt, d.Id;
""",
            command => HrmsDatabase.AddParameter(command, "@SessionId", sessionId),
            reader => new DocumentRow(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetLong(reader, "SessionId"),
                HrmsDatabase.GetLong(reader, "ProtectedFileAssetId"),
                HrmsDatabase.GetString(reader, "OriginalFileName"),
                HrmsDatabase.GetLong(reader, "SizeBytes"),
                HrmsDatabase.GetString(reader, "Sha256"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "DeclaredDocumentType")),
                NullIfEmpty(HrmsDatabase.GetString(reader, "DetectedDocumentType")),
                HrmsDatabase.GetNullableDecimal(reader, "ClassificationConfidence"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "DetectionMethod")),
                HrmsDatabase.GetString(reader, "ProcessingStatus"),
                HrmsDatabase.GetNullableInt(reader, "PageCount"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "ProcessingErrorCode")),
                HrmsDatabase.GetString(reader, "OriginalVerificationStatus"),
                HrmsDatabase.GetNullableInt(reader, "OriginalVerifiedBySystemUserId"),
                HrmsDatabase.GetDateTime(reader, "OriginalVerifiedAt"),
                HrmsDatabase.GetDateOnly(reader, "ReviewedExpiryDate"),
                HrmsDatabase.GetDateTime(reader, "UploadedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "ProcessedAt"),
                NullIfEmpty(HrmsDatabase.GetString(reader, "JobStatus")),
                HrmsDatabase.GetInt(reader, "JobAttemptCount"),
                HrmsDatabase.GetInt(reader, "JobMaxAttempts"),
                HrmsDatabase.GetDateTime(reader, "JobLockedAt"),
                HrmsDatabase.GetDateTime(reader, "JobNextAttemptAt"),
                HrmsDatabase.GetInt(reader, "JobsAhead"),
                HrmsDatabase.GetInt(reader, "ActiveQueueCount"),
                HrmsDatabase.GetInt(reader, "SuccessfulDurationSamples"),
                HrmsDatabase.GetNullableLong(reader, "AverageSuccessfulDurationMs")));
    }

    public static async Task<bool> RequeueFailedDocumentAsync(
        ApplicationDbContext db,
        long sessionId,
        long documentId)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
UPDATE dbo.OnboardingDocuments
SET ProcessingStatus = 'Queued',
    ProcessingErrorCode = NULL,
    ProcessedAt = NULL
WHERE Id = @DocumentId
  AND SessionId = @SessionId
  AND ProcessingStatus = 'Failed';

SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@DocumentId", documentId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var session = await GetSessionAsync(db, sessionId);
        if (session is null)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.PeopleAiJobs
    WHERE OnboardingDocumentId = @DocumentId
      AND Status IN ('Queued', 'Retry', 'Processing')
)
BEGIN
    INSERT INTO dbo.PeopleAiJobs
        (CompanyId, SessionId, OnboardingDocumentId,
         JobType, IdempotencyKey, Status, NextAttemptAt)
    VALUES
        (@CompanyId, @SessionId, @DocumentId,
         'ProcessOnboardingDocument',
         CONCAT('onboarding-document:', @DocumentId,
                ':manual-retry:', CONVERT(nvarchar(36), NEWID())),
         'Queued', SYSUTCDATETIME());
END;

UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Queued',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @SessionId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", session.CompanyId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@DocumentId", documentId);
            });

        await transaction.CommitAsync();
        return true;
    }

    public static Task RecoverStaleJobsAsync(
        ApplicationDbContext db,
        TimeSpan staleAfter) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PeopleAiJobs
SET Status = CASE
        WHEN AttemptCount >= MaxAttempts THEN 'Failed'
        ELSE 'Retry'
    END,
    NextAttemptAt = CASE
        WHEN AttemptCount >= MaxAttempts THEN NULL
        ELSE SYSUTCDATETIME()
    END,
    LockedAt = NULL,
    LockedBy = NULL,
    LastErrorCode = 'STALE_WORKER_RECOVERY',
    UpdatedAt = SYSUTCDATETIME()
WHERE Status = 'Processing'
  AND LockedAt IS NOT NULL
  AND LockedAt < DATEADD(second, -@StaleSeconds, SYSUTCDATETIME());

-- Defensive normalization: a Retry/Queued row that already exhausted every
-- attempt can never be claimed again. Mark it terminal instead of leaving an
-- invisible dead item in the queue forever.
UPDATE dbo.PeopleAiJobs
SET Status = 'Failed',
    NextAttemptAt = NULL,
    LockedAt = NULL,
    LockedBy = NULL,
    LastErrorCode = CASE
        WHEN LastErrorCode IS NULL OR LTRIM(RTRIM(LastErrorCode)) = ''
            THEN 'MAX_ATTEMPTS_EXHAUSTED'
        ELSE LastErrorCode
    END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Status IN ('Queued', 'Retry')
  AND AttemptCount >= MaxAttempts;

UPDATE d
SET ProcessingStatus = CASE
        WHEN j.Status = 'Failed' THEN 'Failed'
        ELSE 'Queued'
    END,
    ProcessingErrorCode = CASE
        WHEN j.Status = 'Failed' THEN 'STALE_WORKER_RECOVERY'
        ELSE NULL
    END
FROM dbo.OnboardingDocuments d
JOIN dbo.PeopleAiJobs j
  ON j.OnboardingDocumentId = d.Id
WHERE j.LastErrorCode = 'STALE_WORKER_RECOVERY'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.PeopleAiJobs newer
      WHERE newer.OnboardingDocumentId = j.OnboardingDocumentId
        AND newer.Id > j.Id
  )
  AND d.ProcessingStatus = 'Processing';

UPDATE d
SET ProcessingStatus = 'Failed',
    ProcessingErrorCode = COALESCE(NULLIF(j.LastErrorCode, ''), 'MAX_ATTEMPTS_EXHAUSTED')
FROM dbo.OnboardingDocuments d
JOIN dbo.PeopleAiJobs j
  ON j.OnboardingDocumentId = d.Id
WHERE j.Status = 'Failed'
  AND j.AttemptCount >= j.MaxAttempts
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.PeopleAiJobs newer
      WHERE newer.OnboardingDocumentId = j.OnboardingDocumentId
        AND newer.Id > j.Id
  )
  AND d.ProcessingStatus IN ('Queued', 'Processing');
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@StaleSeconds",
                Math.Max(60, (int)staleAfter.TotalSeconds)));

    public static async Task<JobRow?> ClaimNextJobAsync(
        ApplicationDbContext db,
        string workerId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SET NOCOUNT ON;

-- OCR is GPU-bound. Multiple web instances may share this database, so the
-- queue claim itself enforces a single active OCR job globally. The short
-- SQL application lock only serializes the claim operation; it is released
-- immediately and does not stay held during OCR.
DECLARE @AppLockResult int;
EXEC @AppLockResult = sys.sp_getapplock
    @Resource = N'ZYNORA:PeopleAI:OCRQueueClaim',
    @LockMode = 'Exclusive',
    @LockOwner = 'Session',
    @LockTimeout = 0;

IF @AppLockResult >= 0
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.PeopleAiJobs
        WHERE Status = 'Processing'
    )
    BEGIN
        ;WITH next_job AS
        (
            SELECT TOP (1) *
            FROM dbo.PeopleAiJobs WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE Status IN ('Queued', 'Retry')
              AND (NextAttemptAt IS NULL OR NextAttemptAt <= SYSUTCDATETIME())
              AND AttemptCount < MaxAttempts
            ORDER BY CreatedAt, Id
        )
        UPDATE next_job
        SET Status = 'Processing',
            AttemptCount = AttemptCount + 1,
            LockedAt = SYSUTCDATETIME(),
            LockedBy = @WorkerId,
            UpdatedAt = SYSUTCDATETIME()
        OUTPUT INSERTED.Id, INSERTED.CompanyId, INSERTED.SessionId,
               INSERTED.OnboardingDocumentId, INSERTED.JobType,
               INSERTED.IdempotencyKey, INSERTED.Status,
               INSERTED.AttemptCount, INSERTED.MaxAttempts;
    END;

    EXEC sys.sp_releaseapplock
        @Resource = N'ZYNORA:PeopleAI:OCRQueueClaim',
        @LockOwner = 'Session';
END;
""",
            command => HrmsDatabase.AddParameter(command, "@WorkerId", workerId),
            reader => new JobRow(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetNullableLong(reader, "SessionId"),
                HrmsDatabase.GetNullableLong(reader, "OnboardingDocumentId"),
                HrmsDatabase.GetString(reader, "JobType"),
                HrmsDatabase.GetString(reader, "IdempotencyKey"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetInt(reader, "AttemptCount"),
                HrmsDatabase.GetInt(reader, "MaxAttempts")));

        return rows.FirstOrDefault();
    }

    public static Task CompleteJobAsync(
        ApplicationDbContext db,
        long jobId) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PeopleAiJobs
SET Status = 'Completed',
    LockedAt = NULL,
    LockedBy = NULL,
    LastErrorCode = NULL,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND Status = 'Processing';
""",
            command => HrmsDatabase.AddParameter(command, "@Id", jobId));

    public static Task FailJobAsync(
        ApplicationDbContext db,
        long jobId,
        string errorCode,
        TimeSpan retryAfter) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PeopleAiJobs
SET Status = CASE WHEN AttemptCount >= MaxAttempts THEN 'Failed' ELSE 'Retry' END,
    NextAttemptAt = CASE WHEN AttemptCount >= MaxAttempts
                         THEN NULL ELSE @NextAttemptAt END,
    LockedAt = NULL,
    LockedBy = NULL,
    LastErrorCode = @ErrorCode,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND Status = 'Processing';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", jobId);
                HrmsDatabase.AddParameter(command, "@ErrorCode", errorCode);
                HrmsDatabase.AddParameter(
                    command, "@NextAttemptAt", DateTime.UtcNow.Add(retryAfter));
            });

    public static Task CancelSessionAsync(
        ApplicationDbContext db,
        long sessionId) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.EmployeeOnboardingSessions
SET Status = 'Cancelled',
    CancelledAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id
  AND Status NOT IN ('Completed', 'Cancelled');

UPDATE dbo.PeopleAiJobs
SET Status = 'Cancelled',
    UpdatedAt = SYSUTCDATETIME()
WHERE SessionId = @Id
  AND Status IN ('Queued', 'Retry');
""",
            command => HrmsDatabase.AddParameter(command, "@Id", sessionId));

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
