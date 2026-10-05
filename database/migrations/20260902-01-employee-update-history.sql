-- Migration: 20260902-01-employee-update-history
-- Additive repair for fresh databases and older employee-update tables.
-- No existing employee, update, or compensation records are modified.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.EmployeeUpdateBatches', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.EmployeeUpdateBatches
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeUpdateBatches PRIMARY KEY,
            EmployeeId int NOT NULL,
            SectionKey nvarchar(80) NOT NULL,
            SectionName nvarchar(150) NOT NULL,
            Status nvarchar(40) NOT NULL CONSTRAINT DF_EmployeeUpdateBatches_Status DEFAULT(N'Open'),
            RequestedBy nvarchar(150) NULL,
            RequestedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeUpdateBatches_RequestedAt DEFAULT(SYSUTCDATETIME()),
            EffectiveDate date NULL,
            IsRetroactive bit NULL,
            LockedBy nvarchar(150) NULL,
            LockedAt datetime2 NULL,
            Note nvarchar(max) NULL,
            AttachmentName nvarchar(260) NULL,
            AttachmentPath nvarchar(500) NULL
        );
    END;
    ELSE
    BEGIN
        IF COL_LENGTH('dbo.EmployeeUpdateBatches', 'EffectiveDate') IS NULL
            ALTER TABLE dbo.EmployeeUpdateBatches ADD EffectiveDate date NULL;
        IF COL_LENGTH('dbo.EmployeeUpdateBatches', 'IsRetroactive') IS NULL
            ALTER TABLE dbo.EmployeeUpdateBatches ADD IsRetroactive bit NULL;
        IF COL_LENGTH('dbo.EmployeeUpdateBatches', 'AttachmentName') IS NULL
            ALTER TABLE dbo.EmployeeUpdateBatches ADD AttachmentName nvarchar(260) NULL;
        IF COL_LENGTH('dbo.EmployeeUpdateBatches', 'AttachmentPath') IS NULL
            ALTER TABLE dbo.EmployeeUpdateBatches ADD AttachmentPath nvarchar(500) NULL;
    END;

    IF OBJECT_ID(N'dbo.EmployeeUpdateChanges', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.EmployeeUpdateChanges
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeUpdateChanges PRIMARY KEY,
            BatchId int NOT NULL,
            FieldKey nvarchar(100) NOT NULL,
            FieldLabel nvarchar(150) NOT NULL,
            OldValue nvarchar(max) NULL,
            NewValue nvarchar(max) NULL
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.EmployeeUpdateBatches')
          AND name = N'IX_EmployeeUpdateBatches_Employee_RequestedAt')
        CREATE INDEX IX_EmployeeUpdateBatches_Employee_RequestedAt
            ON dbo.EmployeeUpdateBatches(EmployeeId, RequestedAt DESC);

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.EmployeeUpdateChanges')
          AND name = N'IX_EmployeeUpdateChanges_BatchId')
        CREATE INDEX IX_EmployeeUpdateChanges_BatchId ON dbo.EmployeeUpdateChanges(BatchId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
