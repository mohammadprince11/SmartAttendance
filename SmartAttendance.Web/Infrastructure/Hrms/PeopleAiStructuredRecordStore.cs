using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleAiStructuredRecordStore
{
    public const decimal HighConfidenceThreshold = 0.90m;

    public sealed record StructuredRecordRow(
        long Id,
        long SessionId,
        long DocumentId,
        long ExtractionRunId,
        string RecordType,
        int SequenceNo,
        string Title,
        string? Subtitle,
        string? Country,
        string? RefNo,
        DateOnly? FromDate,
        DateOnly? ToDate,
        bool IsCurrent,
        string? Note,
        decimal? ProviderConfidence,
        string ExtractionMethod,
        string ReviewStatus,
        string? ReviewedTitle,
        string? ReviewedSubtitle,
        string? ReviewedCountry,
        string? ReviewedRefNo,
        DateOnly? ReviewedFromDate,
        DateOnly? ReviewedToDate,
        bool? ReviewedIsCurrent,
        string? ReviewedNote,
        long DocumentIdForDisplay,
        string DocumentName)
    {
        public string EffectiveTitle =>
            ReviewStatus == "Rejected"
                ? string.Empty
                : ReviewedTitle ?? Title;

        public string? EffectiveSubtitle =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedSubtitle ?? Subtitle;

        public string? EffectiveCountry =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedCountry ?? Country;

        public string? EffectiveRefNo =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedRefNo ?? RefNo;

        public DateOnly? EffectiveFromDate =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedFromDate ?? FromDate;

        public DateOnly? EffectiveToDate =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedToDate ?? ToDate;

        public bool EffectiveIsCurrent =>
            ReviewStatus == "Rejected"
                ? false
                : ReviewedIsCurrent ?? IsCurrent;

        public string? EffectiveNote =>
            ReviewStatus == "Rejected"
                ? null
                : ReviewedNote ?? Note;
    }

    public sealed record ReviewInput(
        string? Title,
        string? Subtitle,
        string? Country,
        string? RefNo,
        DateOnly? FromDate,
        DateOnly? ToDate,
        bool IsCurrent,
        string? Note);

    public static async Task ReplaceForRunAsync(
        ApplicationDbContext db,
        long sessionId,
        long documentId,
        long extractionRunId,
        IReadOnlyCollection<CvStructuredRecord> records)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
DELETE FROM dbo.OnboardingStructuredRecords
WHERE ExtractionRunId = @RunId;
""",
            command =>
                HrmsDatabase.AddParameter(
                    command,
                    "@RunId",
                    extractionRunId));

        var sequenceByType =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (!IsSupportedType(record.RecordType) ||
                string.IsNullOrWhiteSpace(record.Title))
            {
                continue;
            }

            var sequence =
                sequenceByType.TryGetValue(
                    record.RecordType,
                    out var existing)
                    ? existing + 1
                    : 1;
            sequenceByType[record.RecordType] = sequence;

            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO dbo.OnboardingStructuredRecords
(
    SessionId,
    OnboardingDocumentId,
    ExtractionRunId,
    RecordType,
    SequenceNo,
    Title,
    Subtitle,
    Country,
    RefNo,
    FromDate,
    ToDate,
    IsCurrent,
    Note,
    ProviderConfidence,
    ExtractionMethod,
    ReviewStatus
)
VALUES
(
    @SessionId,
    @DocumentId,
    @RunId,
    @RecordType,
    @SequenceNo,
    @Title,
    @Subtitle,
    @Country,
    @RefNo,
    @FromDate,
    @ToDate,
    @IsCurrent,
    @Note,
    @Confidence,
    N'CV_SECTION',
    N'Pending'
);
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
                        documentId);
                    HrmsDatabase.AddParameter(
                        command,
                        "@RunId",
                        extractionRunId);
                    HrmsDatabase.AddParameter(
                        command,
                        "@RecordType",
                        record.RecordType);
                    HrmsDatabase.AddParameter(
                        command,
                        "@SequenceNo",
                        sequence);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Title",
                        Limit(record.Title, 300));
                    HrmsDatabase.AddParameter(
                        command,
                        "@Subtitle",
                        Limit(record.Subtitle, 300));
                    HrmsDatabase.AddParameter(
                        command,
                        "@Country",
                        Limit(record.Country, 120));
                    HrmsDatabase.AddParameter(
                        command,
                        "@RefNo",
                        Limit(record.RefNo, 120));
                    HrmsDatabase.AddParameter(
                        command,
                        "@FromDate",
                        record.FromDate);
                    HrmsDatabase.AddParameter(
                        command,
                        "@ToDate",
                        record.ToDate);
                    HrmsDatabase.AddParameter(
                        command,
                        "@IsCurrent",
                        record.IsCurrent);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Note",
                        Limit(record.Note, 1000));
                    HrmsDatabase.AddParameter(
                        command,
                        "@Confidence",
                        record.Confidence);
                });
        }
    }

    public static Task<List<StructuredRecordRow>> ListLatestAsync(
        ApplicationDbContext db,
        long sessionId) =>
        HrmsDatabase.QueryAsync(
            db,
            """
WITH LatestRuns AS
(
    SELECT
        r.Id,
        r.OnboardingDocumentId,
        ROW_NUMBER() OVER
        (
            PARTITION BY r.OnboardingDocumentId
            ORDER BY r.Id DESC
        ) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
)
SELECT
    sr.Id,
    sr.SessionId,
    sr.OnboardingDocumentId,
    sr.ExtractionRunId,
    sr.RecordType,
    sr.SequenceNo,
    sr.Title,
    sr.Subtitle,
    sr.Country,
    sr.RefNo,
    sr.FromDate,
    sr.ToDate,
    sr.IsCurrent,
    sr.Note,
    sr.ProviderConfidence,
    sr.ExtractionMethod,
    sr.ReviewStatus,
    sr.ReviewedTitle,
    sr.ReviewedSubtitle,
    sr.ReviewedCountry,
    sr.ReviewedRefNo,
    sr.ReviewedFromDate,
    sr.ReviewedToDate,
    sr.ReviewedIsCurrent,
    sr.ReviewedNote,
    d.Id AS DocumentIdForDisplay,
    a.OriginalFileName AS DocumentName
FROM LatestRuns lr
JOIN dbo.OnboardingStructuredRecords sr
  ON sr.ExtractionRunId = lr.Id
JOIN dbo.OnboardingDocuments d
  ON d.Id = sr.OnboardingDocumentId
JOIN dbo.ProtectedFileAssets a
  ON a.Id = d.ProtectedFileAssetId
WHERE lr.rn = 1
  AND sr.SessionId = @SessionId
ORDER BY
    CASE sr.RecordType
        WHEN N'Experience' THEN 1
        WHEN N'Education' THEN 2
        WHEN N'Certificate' THEN 3
        ELSE 9
    END,
    sr.SequenceNo,
    sr.Id;
""",
            command =>
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId),
            reader => new StructuredRecordRow(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetLong(reader, "SessionId"),
                HrmsDatabase.GetLong(
                    reader,
                    "OnboardingDocumentId"),
                HrmsDatabase.GetLong(
                    reader,
                    "ExtractionRunId"),
                HrmsDatabase.GetString(reader, "RecordType"),
                HrmsDatabase.GetInt(reader, "SequenceNo"),
                HrmsDatabase.GetString(reader, "Title"),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "Subtitle")),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "Country")),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "RefNo")),
                HrmsDatabase.GetDateOnly(reader, "FromDate"),
                HrmsDatabase.GetDateOnly(reader, "ToDate"),
                HrmsDatabase.GetBool(reader, "IsCurrent"),
                NullIfEmpty(
                    HrmsDatabase.GetString(reader, "Note")),
                HrmsDatabase.GetNullableDecimal(
                    reader,
                    "ProviderConfidence"),
                HrmsDatabase.GetString(
                    reader,
                    "ExtractionMethod"),
                HrmsDatabase.GetString(
                    reader,
                    "ReviewStatus"),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "ReviewedTitle")),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "ReviewedSubtitle")),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "ReviewedCountry")),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "ReviewedRefNo")),
                HrmsDatabase.GetDateOnly(
                    reader,
                    "ReviewedFromDate"),
                HrmsDatabase.GetDateOnly(
                    reader,
                    "ReviewedToDate"),
                GetNullableBool(
                    reader,
                    "ReviewedIsCurrent"),
                NullIfEmpty(
                    HrmsDatabase.GetString(
                        reader,
                        "ReviewedNote")),
                HrmsDatabase.GetLong(
                    reader,
                    "DocumentIdForDisplay"),
                HrmsDatabase.GetString(
                    reader,
                    "DocumentName")));


    public static async Task<bool> HasPendingLatestAsync(
        ApplicationDbContext db,
        long sessionId)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
WITH LatestRuns AS
(
    SELECT
        r.Id,
        r.OnboardingDocumentId,
        ROW_NUMBER() OVER
        (
            PARTITION BY r.OnboardingDocumentId
            ORDER BY r.Id DESC
        ) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
)
SELECT COUNT(*)
FROM LatestRuns lr
JOIN dbo.OnboardingStructuredRecords sr
  ON sr.ExtractionRunId = lr.Id
WHERE lr.rn = 1
  AND sr.SessionId = @SessionId
  AND sr.ReviewStatus = N'Pending';
""",
            command =>
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId));

        return count > 0;
    }

    public static Task ReviewAsync(
        ApplicationDbContext db,
        long sessionId,
        long recordId,
        int systemUserId,
        string action,
        ReviewInput input)
    {
        var title = Limit(input.Title, 300);

        if (action == "Modify" &&
            string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException(
                "Structured record title is required.");
        }

        return HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE sr
SET ReviewStatus = @ReviewStatus,
    ReviewedTitle =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.Title
            ELSE @Title
        END,
    ReviewedSubtitle =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.Subtitle
            ELSE @Subtitle
        END,
    ReviewedCountry =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.Country
            ELSE @Country
        END,
    ReviewedRefNo =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.RefNo
            ELSE @RefNo
        END,
    ReviewedFromDate =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.FromDate
            ELSE @FromDate
        END,
    ReviewedToDate =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.ToDate
            ELSE @ToDate
        END,
    ReviewedIsCurrent =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.IsCurrent
            ELSE @IsCurrent
        END,
    ReviewedNote =
        CASE
            WHEN @ReviewStatus = N'Rejected' THEN NULL
            WHEN @ReviewStatus = N'Accepted' THEN sr.Note
            ELSE @Note
        END,
    ReviewedBySystemUserId = @ReviewerId,
    ReviewedAt = SYSUTCDATETIME()
FROM dbo.OnboardingStructuredRecords sr
WHERE sr.Id = @RecordId
  AND sr.SessionId = @SessionId
  AND EXISTS
  (
      SELECT 1
      FROM dbo.DocumentExtractionRuns r
      WHERE r.Id = sr.ExtractionRunId
        AND r.Id =
        (
            SELECT TOP (1) latest.Id
            FROM dbo.DocumentExtractionRuns latest
            WHERE latest.OnboardingDocumentId =
                  sr.OnboardingDocumentId
            ORDER BY latest.Id DESC
        )
  );
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewStatus",
                    action switch
                    {
                        "Accept" => "Accepted",
                        "Modify" => "Modified",
                        "Reject" => "Rejected",
                        _ => throw new InvalidOperationException(
                            "Invalid structured record review action.")
                    });
                HrmsDatabase.AddParameter(
                    command,
                    "@Title",
                    title);
                HrmsDatabase.AddParameter(
                    command,
                    "@Subtitle",
                    Limit(input.Subtitle, 300));
                HrmsDatabase.AddParameter(
                    command,
                    "@Country",
                    Limit(input.Country, 120));
                HrmsDatabase.AddParameter(
                    command,
                    "@RefNo",
                    Limit(input.RefNo, 120));
                HrmsDatabase.AddParameter(
                    command,
                    "@FromDate",
                    input.FromDate);
                HrmsDatabase.AddParameter(
                    command,
                    "@ToDate",
                    input.ToDate);
                HrmsDatabase.AddParameter(
                    command,
                    "@IsCurrent",
                    input.IsCurrent);
                HrmsDatabase.AddParameter(
                    command,
                    "@Note",
                    Limit(input.Note, 1000));
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewerId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@RecordId",
                    recordId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
            });
    }

    public static Task AcceptHighConfidenceAsync(
        ApplicationDbContext db,
        long sessionId,
        int systemUserId) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE sr
SET ReviewStatus = N'Accepted',
    ReviewedTitle = sr.Title,
    ReviewedSubtitle = sr.Subtitle,
    ReviewedCountry = sr.Country,
    ReviewedRefNo = sr.RefNo,
    ReviewedFromDate = sr.FromDate,
    ReviewedToDate = sr.ToDate,
    ReviewedIsCurrent = sr.IsCurrent,
    ReviewedNote = sr.Note,
    ReviewedBySystemUserId = @ReviewerId,
    ReviewedAt = SYSUTCDATETIME()
FROM dbo.OnboardingStructuredRecords sr
WHERE sr.SessionId = @SessionId
  AND sr.ReviewStatus = N'Pending'
  AND sr.ProviderConfidence >= @Threshold
  AND EXISTS
  (
      SELECT 1
      FROM dbo.DocumentExtractionRuns r
      WHERE r.Id = sr.ExtractionRunId
        AND r.Id =
        (
            SELECT TOP (1) latest.Id
            FROM dbo.DocumentExtractionRuns latest
            WHERE latest.OnboardingDocumentId =
                  sr.OnboardingDocumentId
            ORDER BY latest.Id DESC
        )
  );
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@ReviewerId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
                HrmsDatabase.AddParameter(
                    command,
                    "@Threshold",
                    HighConfidenceThreshold);
            });

    public static Task PromoteAcceptedAsync(
        ApplicationDbContext db,
        long sessionId,
        int employeeId,
        string createdBy) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
WITH LatestRuns AS
(
    SELECT
        r.Id,
        r.OnboardingDocumentId,
        ROW_NUMBER() OVER
        (
            PARTITION BY r.OnboardingDocumentId
            ORDER BY r.Id DESC
        ) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
)
INSERT INTO dbo.EmployeeFileRecords
(
    EmployeeId,
    RecordType,
    Title,
    Subtitle,
    Country,
    RefNo,
    FromDate,
    ToDate,
    IsCurrent,
    IsReturned,
    EmployeeAcknowledged,
    Note,
    CreatedAt,
    CreatedBy,
    IsDeleted
)
SELECT
    @EmployeeId,
    CASE sr.RecordType
        WHEN N'Education' THEN 1
        WHEN N'Experience' THEN 2
        WHEN N'Certificate' THEN 3
    END,
    COALESCE(sr.ReviewedTitle, sr.Title),
    COALESCE(sr.ReviewedSubtitle, sr.Subtitle),
    COALESCE(sr.ReviewedCountry, sr.Country),
    COALESCE(sr.ReviewedRefNo, sr.RefNo),
    COALESCE(sr.ReviewedFromDate, sr.FromDate),
    COALESCE(sr.ReviewedToDate, sr.ToDate),
    COALESCE(sr.ReviewedIsCurrent, sr.IsCurrent),
    0,
    0,
    COALESCE(sr.ReviewedNote, sr.Note),
    SYSUTCDATETIME(),
    @CreatedBy,
    0
FROM LatestRuns lr
JOIN dbo.OnboardingStructuredRecords sr
  ON sr.ExtractionRunId = lr.Id
WHERE lr.rn = 1
  AND sr.SessionId = @SessionId
  AND sr.ReviewStatus IN (N'Accepted', N'Modified')
  AND sr.RecordType IN
      (N'Education', N'Experience', N'Certificate');

;WITH LatestRuns AS
(
    SELECT
        r.Id,
        r.OnboardingDocumentId,
        ROW_NUMBER() OVER
        (
            PARTITION BY r.OnboardingDocumentId
            ORDER BY r.Id DESC
        ) AS rn
    FROM dbo.DocumentExtractionRuns r
    JOIN dbo.OnboardingDocuments d
      ON d.Id = r.OnboardingDocumentId
    WHERE d.SessionId = @SessionId
),
AddressCandidate AS
(
    SELECT TOP (1)
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.ReviewedValue)), N''),
            NULLIF(LTRIM(RTRIM(f.NormalizedValue)), N''),
            NULLIF(LTRIM(RTRIM(f.RawValue)), N'')
        ) AS AddressValue
    FROM LatestRuns lr
    JOIN dbo.DocumentExtractedFields f
      ON f.ExtractionRunId = lr.Id
    WHERE lr.rn = 1
      AND f.FieldKey = N'Address'
      AND f.ReviewStatus IN (N'Accepted', N'Modified')
    ORDER BY
        CASE f.ReviewStatus
            WHEN N'Modified' THEN 0
            ELSE 1
        END,
        f.ProviderConfidence DESC,
        f.Id DESC
)
INSERT INTO dbo.EmployeeFileRecords
(
    EmployeeId,
    RecordType,
    Title,
    Subtitle,
    IsCurrent,
    IsReturned,
    EmployeeAcknowledged,
    CreatedAt,
    CreatedBy,
    IsDeleted
)
SELECT
    @EmployeeId,
    7,
    N'Primary Address',
    ac.AddressValue,
    1,
    0,
    0,
    SYSUTCDATETIME(),
    @CreatedBy,
    0
FROM AddressCandidate ac
WHERE ac.AddressValue IS NOT NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SessionId",
                    sessionId);
                HrmsDatabase.AddParameter(
                    command,
                    "@CreatedBy",
                    Limit(createdBy, 150) ?? "HR");
            });

    private static bool IsSupportedType(string value) =>
        value is "Education" or "Experience" or "Certificate";

    private static string? Limit(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value;

    private static bool? GetNullableBool(
        System.Data.Common.DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToBoolean(reader.GetValue(ordinal));
    }
}
