-- Explicit controlled migration. Never execute as part of a web request.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.EmployeePolls', 'U') IS NULL
    THROW 51000, 'EmployeePolls must exist before this migration.', 1;
IF COL_LENGTH('dbo.EmployeePolls', 'StartsOn') IS NULL
    ALTER TABLE dbo.EmployeePolls ADD StartsOn date NULL;
IF COL_LENGTH('dbo.EmployeePolls', 'EndsOn') IS NULL
    ALTER TABLE dbo.EmployeePolls ADD EndsOn date NULL;
IF COL_LENGTH('dbo.EmployeePolls', 'ConfidentialResults') IS NULL
    ALTER TABLE dbo.EmployeePolls ADD ConfidentialResults bit NOT NULL CONSTRAINT DF_EmployeePolls_ConfidentialResults DEFAULT(1);
COMMIT;
