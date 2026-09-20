using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Phase 0 foundation for People AI. These tables store protected onboarding,
/// extraction review, deterministic validation and AI audit metadata.
/// Nothing here writes directly to Employees.
/// </summary>
public static class PeopleAiSchema
{
    public const string FoundationMigrationId =
        "20260919-01-people-ai-foundation";

    public const string DocumentProcessingContractMigrationId =
        "20260919-02-people-ai-document-processing-contract";

    public const string CvIntelligenceMigrationId =
        "20260920-03-people-ai-cv-intelligence";

    public static readonly string CvIntelligenceMigrationSql = """
IF OBJECT_ID('dbo.OnboardingStructuredRecords', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OnboardingStructuredRecords
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SessionId bigint NOT NULL,
        OnboardingDocumentId bigint NOT NULL,
        ExtractionRunId bigint NOT NULL,
        RecordType nvarchar(30) NOT NULL,
        SequenceNo int NOT NULL,
        Title nvarchar(300) NOT NULL,
        Subtitle nvarchar(300) NULL,
        Country nvarchar(120) NULL,
        RefNo nvarchar(120) NULL,
        FromDate date NULL,
        ToDate date NULL,
        IsCurrent bit NOT NULL CONSTRAINT DF_OnboardingStructuredRecords_Current DEFAULT(0),
        Note nvarchar(1000) NULL,
        ProviderConfidence decimal(6,5) NULL,
        ExtractionMethod nvarchar(30) NOT NULL,
        ReviewStatus nvarchar(30) NOT NULL CONSTRAINT DF_OnboardingStructuredRecords_Review DEFAULT('Pending'),
        ReviewedTitle nvarchar(300) NULL,
        ReviewedSubtitle nvarchar(300) NULL,
        ReviewedCountry nvarchar(120) NULL,
        ReviewedRefNo nvarchar(120) NULL,
        ReviewedFromDate date NULL,
        ReviewedToDate date NULL,
        ReviewedIsCurrent bit NULL,
        ReviewedNote nvarchar(1000) NULL,
        ReviewedBySystemUserId int NULL,
        ReviewedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_OnboardingStructuredRecords_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_OnboardingStructuredRecords_Session
            FOREIGN KEY (SessionId) REFERENCES dbo.EmployeeOnboardingSessions(Id),
        CONSTRAINT FK_OnboardingStructuredRecords_Document
            FOREIGN KEY (OnboardingDocumentId) REFERENCES dbo.OnboardingDocuments(Id),
        CONSTRAINT FK_OnboardingStructuredRecords_Run
            FOREIGN KEY (ExtractionRunId) REFERENCES dbo.DocumentExtractionRuns(Id)
    );

    CREATE UNIQUE INDEX UX_OnboardingStructuredRecords_RunSequence
        ON dbo.OnboardingStructuredRecords(ExtractionRunId, RecordType, SequenceNo);

    CREATE INDEX IX_OnboardingStructuredRecords_SessionReview
        ON dbo.OnboardingStructuredRecords(SessionId, ReviewStatus, RecordType);
END;
""";

    public static readonly string DocumentProcessingContractMigrationSql = """
IF COL_LENGTH('dbo.OnboardingDocuments', 'DetectionMethod') IS NULL
    ALTER TABLE dbo.OnboardingDocuments
        ADD DetectionMethod nvarchar(50) NULL;
""";

    public static readonly string FoundationMigrationSql = """
IF OBJECT_ID('dbo.EmployeeOnboardingSessions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeOnboardingSessions
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        CreatedBySystemUserId int NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_EmployeeOnboardingSessions_Status DEFAULT('Draft'),
        CreatedEmployeeId int NULL,
        FailureCode nvarchar(100) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeOnboardingSessions_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeOnboardingSessions_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CompletedAt datetime2 NULL,
        CancelledAt datetime2 NULL,
        ExpiresAt datetime2 NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_EmployeeOnboardingSessions_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id),
        CONSTRAINT FK_EmployeeOnboardingSessions_Employee
            FOREIGN KEY (CreatedEmployeeId) REFERENCES dbo.Employees(Id)
    );
    CREATE INDEX IX_EmployeeOnboardingSessions_CompanyStatus
        ON dbo.EmployeeOnboardingSessions(CompanyId, Status, CreatedAt DESC);
END;

IF OBJECT_ID('dbo.ProtectedFileAssets', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProtectedFileAssets
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        OwnerType nvarchar(40) NOT NULL,
        OwnerId bigint NOT NULL,
        StorageKey nvarchar(500) NOT NULL,
        OriginalFileName nvarchar(260) NOT NULL,
        MimeType nvarchar(120) NULL,
        Extension nvarchar(20) NULL,
        SizeBytes bigint NOT NULL,
        Sha256 char(64) NULL,
        SignatureValidationStatus nvarchar(30) NOT NULL,
        MalwareScanStatus nvarchar(30) NOT NULL,
        CreatedBySystemUserId int NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_ProtectedFileAssets_CreatedAt DEFAULT(SYSUTCDATETIME()),
        DeletedAt datetime2 NULL,
        CONSTRAINT FK_ProtectedFileAssets_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );
    CREATE UNIQUE INDEX UX_ProtectedFileAssets_StorageKey
        ON dbo.ProtectedFileAssets(StorageKey);
    CREATE INDEX IX_ProtectedFileAssets_Owner
        ON dbo.ProtectedFileAssets(CompanyId, OwnerType, OwnerId, DeletedAt);
END;

IF OBJECT_ID('dbo.OnboardingDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OnboardingDocuments
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SessionId bigint NOT NULL,
        ProtectedFileAssetId bigint NOT NULL,
        DeclaredDocumentType nvarchar(50) NULL,
        DetectedDocumentType nvarchar(50) NULL,
        ClassificationConfidence decimal(6,5) NULL,
        DetectionMethod nvarchar(50) NULL,
        ProcessingStatus nvarchar(30) NOT NULL CONSTRAINT DF_OnboardingDocuments_Status DEFAULT('Uploaded'),
        PageCount int NULL,
        ProcessingErrorCode nvarchar(100) NULL,
        OriginalVerificationStatus nvarchar(30) NOT NULL
            CONSTRAINT DF_OnboardingDocuments_OriginalVerification DEFAULT('NotSeen'),
        OriginalVerifiedBySystemUserId int NULL,
        OriginalVerifiedAt datetime2 NULL,
        ReviewedExpiryDate date NULL,
        UploadedAt datetime2 NOT NULL CONSTRAINT DF_OnboardingDocuments_UploadedAt DEFAULT(SYSUTCDATETIME()),
        ProcessedAt datetime2 NULL,
        CONSTRAINT FK_OnboardingDocuments_Session
            FOREIGN KEY (SessionId) REFERENCES dbo.EmployeeOnboardingSessions(Id),
        CONSTRAINT FK_OnboardingDocuments_Asset
            FOREIGN KEY (ProtectedFileAssetId) REFERENCES dbo.ProtectedFileAssets(Id)
    );
    CREATE INDEX IX_OnboardingDocuments_Session
        ON dbo.OnboardingDocuments(SessionId, ProcessingStatus);
END;

IF COL_LENGTH('dbo.OnboardingDocuments', 'OriginalVerificationStatus') IS NULL
BEGIN
    ALTER TABLE dbo.OnboardingDocuments
        ADD OriginalVerificationStatus nvarchar(30) NOT NULL
            CONSTRAINT DF_OnboardingDocuments_OriginalVerification_Upgrade
            DEFAULT('NotSeen');
END;

IF COL_LENGTH('dbo.OnboardingDocuments', 'OriginalVerifiedBySystemUserId') IS NULL
    ALTER TABLE dbo.OnboardingDocuments ADD OriginalVerifiedBySystemUserId int NULL;

IF COL_LENGTH('dbo.OnboardingDocuments', 'OriginalVerifiedAt') IS NULL
    ALTER TABLE dbo.OnboardingDocuments ADD OriginalVerifiedAt datetime2 NULL;

IF COL_LENGTH('dbo.OnboardingDocuments', 'ReviewedExpiryDate') IS NULL
    ALTER TABLE dbo.OnboardingDocuments ADD ReviewedExpiryDate date NULL;

IF COL_LENGTH('dbo.OnboardingDocuments', 'DetectionMethod') IS NULL
    ALTER TABLE dbo.OnboardingDocuments ADD DetectionMethod nvarchar(50) NULL;

IF OBJECT_ID('dbo.DocumentExtractionRuns', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentExtractionRuns
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OnboardingDocumentId bigint NOT NULL,
        Provider nvarchar(80) NOT NULL,
        Model nvarchar(120) NULL,
        ExtractorVersion nvarchar(50) NULL,
        SchemaVersion nvarchar(50) NULL,
        Status nvarchar(30) NOT NULL,
        InputHash char(64) NULL,
        ErrorCode nvarchar(100) NULL,
        StartedAt datetime2 NOT NULL CONSTRAINT DF_DocumentExtractionRuns_StartedAt DEFAULT(SYSUTCDATETIME()),
        CompletedAt datetime2 NULL,
        CONSTRAINT FK_DocumentExtractionRuns_Document
            FOREIGN KEY (OnboardingDocumentId) REFERENCES dbo.OnboardingDocuments(Id)
    );
    CREATE INDEX IX_DocumentExtractionRuns_Document
        ON dbo.DocumentExtractionRuns(OnboardingDocumentId, StartedAt DESC);
END;

IF OBJECT_ID('dbo.DocumentExtractedFields', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentExtractedFields
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ExtractionRunId bigint NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        RawValue nvarchar(max) NULL,
        NormalizedValue nvarchar(1000) NULL,
        ProviderConfidence decimal(6,5) NULL,
        ValidationStatus nvarchar(30) NOT NULL CONSTRAINT DF_DocumentExtractedFields_Validation DEFAULT('Pending'),
        ExtractionMethod nvarchar(30) NOT NULL,
        SourcePage int NULL,
        SourceBoundingBox nvarchar(500) NULL,
        ReviewStatus nvarchar(30) NOT NULL CONSTRAINT DF_DocumentExtractedFields_Review DEFAULT('Pending'),
        ReviewedValue nvarchar(1000) NULL,
        ReviewedBySystemUserId int NULL,
        ReviewedAt datetime2 NULL,
        CONSTRAINT FK_DocumentExtractedFields_Run
            FOREIGN KEY (ExtractionRunId) REFERENCES dbo.DocumentExtractionRuns(Id)
    );
    CREATE INDEX IX_DocumentExtractedFields_RunField
        ON dbo.DocumentExtractedFields(ExtractionRunId, FieldKey);
END;

IF OBJECT_ID('dbo.OnboardingValidationIssues', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OnboardingValidationIssues
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SessionId bigint NOT NULL,
        OnboardingDocumentId bigint NULL,
        RuleCode nvarchar(100) NOT NULL,
        Category nvarchar(50) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        FieldKey nvarchar(100) NULL,
        RelatedFieldKey nvarchar(100) NULL,
        Message nvarchar(1000) NOT NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_OnboardingValidationIssues_Status DEFAULT('Open'),
        ResolvedBySystemUserId int NULL,
        ResolvedAt datetime2 NULL,
        Resolution nvarchar(1000) NULL,
        CONSTRAINT FK_OnboardingValidationIssues_Session
            FOREIGN KEY (SessionId) REFERENCES dbo.EmployeeOnboardingSessions(Id),
        CONSTRAINT FK_OnboardingValidationIssues_Document
            FOREIGN KEY (OnboardingDocumentId) REFERENCES dbo.OnboardingDocuments(Id)
    );
    CREATE INDEX IX_OnboardingValidationIssues_SessionStatus
        ON dbo.OnboardingValidationIssues(SessionId, Status, Severity);
END;

IF OBJECT_ID('dbo.CompanyPeopleAiSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyPeopleAiSettings
    (
        CompanyId int NOT NULL PRIMARY KEY,
        DuplicateScope nvarchar(40) NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_DuplicateScope DEFAULT('AuthorizedCompanies'),
        DuplicateAction nvarchar(30) NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_DuplicateAction DEFAULT('RequireReview'),
        ReviewerMode nvarchar(50) NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_ReviewerMode DEFAULT('AdminOrCreatorWithPermission'),
        EnabledLanguages nvarchar(100) NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_Languages DEFAULT('ar;en'),
        CloudProcessingAllowed bit NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_Cloud DEFAULT(0),
        IsEnabled bit NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_Enabled DEFAULT(1),
        UpdatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_CompanyPeopleAiSettings_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CompanyPeopleAiSettings_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );
END;

IF OBJECT_ID('dbo.CompanyPeopleAiDocumentTypes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyPeopleAiDocumentTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        DocumentType nvarchar(50) NOT NULL,
        DisplayLabel nvarchar(150) NOT NULL,
        SortOrder int NOT NULL
            CONSTRAINT DF_CompanyPeopleAiDocumentTypes_SortOrder
            DEFAULT(100),
        IsActive bit NOT NULL
            CONSTRAINT DF_CompanyPeopleAiDocumentTypes_Active
            DEFAULT(1),
        UpdatedAt datetime2 NOT NULL
            CONSTRAINT DF_CompanyPeopleAiDocumentTypes_UpdatedAt
            DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CompanyPeopleAiDocumentTypes_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );

    CREATE UNIQUE INDEX UX_CompanyPeopleAiDocumentTypes_Key
        ON dbo.CompanyPeopleAiDocumentTypes(CompanyId, DocumentType);

    CREATE INDEX IX_CompanyPeopleAiDocumentTypes_List
        ON dbo.CompanyPeopleAiDocumentTypes(
            CompanyId, IsActive, SortOrder, DisplayLabel);
END;

IF OBJECT_ID('dbo.CompanyEmployeeDocumentPolicies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyEmployeeDocumentPolicies
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        DocumentType nvarchar(50) NOT NULL,
        EmployeeCategory nvarchar(30) NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_Category DEFAULT('All'),
        Requirement nvarchar(30) NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_Requirement DEFAULT('Optional'),
        RequireExpiryDate bit NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_Expiry DEFAULT(0),
        RequireOriginalVerification bit NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_Original DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_Active DEFAULT(1),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_CompanyEmployeeDocumentPolicies_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CompanyEmployeeDocumentPolicies_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );
    CREATE UNIQUE INDEX UX_CompanyEmployeeDocumentPolicies_Key
        ON dbo.CompanyEmployeeDocumentPolicies(CompanyId, DocumentType, EmployeeCategory);
END;

IF OBJECT_ID('dbo.CompanyPeopleAiFieldPolicies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyPeopleAiFieldPolicies
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        DocumentType nvarchar(50) NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        DisplayLabel nvarchar(150) NOT NULL,
        Requirement nvarchar(20) NOT NULL
            CONSTRAINT DF_CompanyPeopleAiFieldPolicies_Requirement
            DEFAULT('Optional'),
        SortOrder int NOT NULL
            CONSTRAINT DF_CompanyPeopleAiFieldPolicies_SortOrder
            DEFAULT(100),
        AllowBulkApprove bit NOT NULL
            CONSTRAINT DF_CompanyPeopleAiFieldPolicies_Bulk
            DEFAULT(1),
        IsActive bit NOT NULL
            CONSTRAINT DF_CompanyPeopleAiFieldPolicies_Active
            DEFAULT(1),
        UpdatedAt datetime2 NOT NULL
            CONSTRAINT DF_CompanyPeopleAiFieldPolicies_UpdatedAt
            DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CompanyPeopleAiFieldPolicies_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );

    CREATE UNIQUE INDEX UX_CompanyPeopleAiFieldPolicies_Key
        ON dbo.CompanyPeopleAiFieldPolicies(
            CompanyId, DocumentType, FieldKey);

    CREATE INDEX IX_CompanyPeopleAiFieldPolicies_Review
        ON dbo.CompanyPeopleAiFieldPolicies(
            CompanyId, DocumentType, IsActive, SortOrder);
END;

IF OBJECT_ID('dbo.PeopleAiJobs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PeopleAiJobs
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        SessionId bigint NULL,
        OnboardingDocumentId bigint NULL,
        JobType nvarchar(60) NOT NULL,
        IdempotencyKey nvarchar(160) NOT NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_PeopleAiJobs_Status DEFAULT('Queued'),
        AttemptCount int NOT NULL CONSTRAINT DF_PeopleAiJobs_Attempts DEFAULT(0),
        MaxAttempts int NOT NULL CONSTRAINT DF_PeopleAiJobs_MaxAttempts DEFAULT(5),
        NextAttemptAt datetime2 NULL,
        LockedAt datetime2 NULL,
        LockedBy nvarchar(120) NULL,
        LastErrorCode nvarchar(100) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_PeopleAiJobs_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_PeopleAiJobs_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_PeopleAiJobs_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id),
        CONSTRAINT FK_PeopleAiJobs_Session
            FOREIGN KEY (SessionId) REFERENCES dbo.EmployeeOnboardingSessions(Id),
        CONSTRAINT FK_PeopleAiJobs_Document
            FOREIGN KEY (OnboardingDocumentId) REFERENCES dbo.OnboardingDocuments(Id)
    );
    CREATE UNIQUE INDEX UX_PeopleAiJobs_Idempotency
        ON dbo.PeopleAiJobs(IdempotencyKey);
    CREATE INDEX IX_PeopleAiJobs_Dequeue
        ON dbo.PeopleAiJobs(Status, NextAttemptAt, CreatedAt);
END;

IF OBJECT_ID('dbo.PeopleAiAuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PeopleAiAuditLogs
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NULL,
        SystemUserId int NULL,
        EmployeeId int NULL,
        SessionId bigint NULL,
        Feature nvarchar(80) NOT NULL,
        Operation nvarchar(80) NOT NULL,
        Provider nvarchar(80) NULL,
        Model nvarchar(120) NULL,
        ToolNames nvarchar(1000) NULL,
        CorrelationId nvarchar(100) NULL,
        Success bit NOT NULL,
        DurationMs int NULL,
        InputUnits int NULL,
        OutputUnits int NULL,
        ErrorCode nvarchar(100) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_PeopleAiAuditLogs_CreatedAt DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_PeopleAiAuditLogs_CompanyCreated
        ON dbo.PeopleAiAuditLogs(CompanyId, CreatedAt DESC);
    CREATE INDEX IX_PeopleAiAuditLogs_EmployeeCreated
        ON dbo.PeopleAiAuditLogs(EmployeeId, CreatedAt DESC);
END;

IF OBJECT_ID('dbo.EmployeeIdentityDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeIdentityDocuments
    (
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        EmployeeId int NOT NULL,
        DocumentType nvarchar(50) NOT NULL,
        CountryCode nvarchar(10) NULL,
        DocumentNumber nvarchar(150) NULL,
        NormalizedDocumentNumber nvarchar(150) NULL,
        NationalNumber nvarchar(150) NULL,
        FamilyNumber nvarchar(150) NULL,
        IssueDate date NULL,
        ExpiryDate date NULL,
        IssuingAuthority nvarchar(200) NULL,
        PlaceOfIssue nvarchar(200) NULL,
        ProtectedFileAssetId bigint NULL,
        ExtractionStatus nvarchar(30) NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_Extraction DEFAULT('NotProcessed'),
        VerificationStatus nvarchar(30) NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_Verification DEFAULT('PendingReview'),
        OriginalVerificationStatus nvarchar(30) NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_Original DEFAULT('NotSeen'),
        OriginalVerifiedBySystemUserId int NULL,
        OriginalVerifiedAt datetime2 NULL,
        IsCurrent bit NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_Current DEFAULT(1),
        ReplacedByDocumentId bigint NULL,
        SourceOnboardingDocumentId bigint NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeIdentityDocuments_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_EmployeeIdentityDocuments_Company
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id),
        CONSTRAINT FK_EmployeeIdentityDocuments_Employee
            FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_EmployeeIdentityDocuments_Asset
            FOREIGN KEY (ProtectedFileAssetId) REFERENCES dbo.ProtectedFileAssets(Id),
        CONSTRAINT FK_EmployeeIdentityDocuments_SourceDocument
            FOREIGN KEY (SourceOnboardingDocumentId) REFERENCES dbo.OnboardingDocuments(Id)
    );
    CREATE INDEX IX_EmployeeIdentityDocuments_EmployeeCurrent
        ON dbo.EmployeeIdentityDocuments(EmployeeId, DocumentType, IsCurrent);
    CREATE INDEX IX_EmployeeIdentityDocuments_CompanyNumber
        ON dbo.EmployeeIdentityDocuments(CompanyId, DocumentType, NormalizedDocumentNumber);
    CREATE INDEX IX_EmployeeIdentityDocuments_Number
        ON dbo.EmployeeIdentityDocuments(DocumentType, NormalizedDocumentNumber);
END;
""";

    /// <summary>
    /// Verifies the deployed People AI schema. This method intentionally does
    /// not execute DDL; schema changes belong to SqlSchemaMigrator.
    /// </summary>
    public static Task VerifyAsync(ApplicationDbContext db) =>
        HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('dbo.EmployeeOnboardingSessions', 'U') IS NULL
    THROW 51000, 'People AI schema missing EmployeeOnboardingSessions. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.ProtectedFileAssets', 'U') IS NULL
    THROW 51000, 'People AI schema missing ProtectedFileAssets. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.OnboardingDocuments', 'U') IS NULL
    THROW 51000, 'People AI schema missing OnboardingDocuments. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.DocumentExtractionRuns', 'U') IS NULL
    THROW 51000, 'People AI schema missing DocumentExtractionRuns. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.DocumentExtractedFields', 'U') IS NULL
    THROW 51000, 'People AI schema missing DocumentExtractedFields. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.OnboardingValidationIssues', 'U') IS NULL
    THROW 51000, 'People AI schema missing OnboardingValidationIssues. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.CompanyPeopleAiSettings', 'U') IS NULL
    THROW 51000, 'People AI schema missing CompanyPeopleAiSettings. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.CompanyPeopleAiDocumentTypes', 'U') IS NULL
    THROW 51000, 'People AI schema missing CompanyPeopleAiDocumentTypes. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.CompanyEmployeeDocumentPolicies', 'U') IS NULL
    THROW 51000, 'People AI schema missing CompanyEmployeeDocumentPolicies. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.CompanyPeopleAiFieldPolicies', 'U') IS NULL
    THROW 51000, 'People AI schema missing CompanyPeopleAiFieldPolicies. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.PeopleAiJobs', 'U') IS NULL
    THROW 51000, 'People AI schema missing PeopleAiJobs. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.PeopleAiAuditLogs', 'U') IS NULL
    THROW 51000, 'People AI schema missing PeopleAiAuditLogs. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.EmployeeIdentityDocuments', 'U') IS NULL
    THROW 51000, 'People AI schema missing EmployeeIdentityDocuments. Apply controlled database migrations before startup.', 1;
IF OBJECT_ID('dbo.OnboardingStructuredRecords', 'U') IS NULL
    THROW 51000, 'People AI schema missing OnboardingStructuredRecords. Apply controlled database migrations before startup.', 1;

IF COL_LENGTH('dbo.OnboardingDocuments', 'OriginalVerificationStatus') IS NULL
   OR COL_LENGTH('dbo.OnboardingDocuments', 'ReviewedExpiryDate') IS NULL
   OR COL_LENGTH('dbo.OnboardingDocuments', 'DetectionMethod') IS NULL
    THROW 51000, 'People AI OnboardingDocuments schema is incomplete. Apply controlled database migrations before startup.', 1;
""");
}
