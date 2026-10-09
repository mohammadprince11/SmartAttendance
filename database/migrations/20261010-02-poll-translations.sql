-- Controlled migration, explicitly executed; never in a request or startup.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.EmployeePolls', 'U') IS NULL
    THROW 51000, 'EmployeePolls must exist before this migration.', 1;
IF COL_LENGTH('dbo.EmployeePolls', 'ContentTranslationsJson') IS NULL
    ALTER TABLE dbo.EmployeePolls ADD ContentTranslationsJson nvarchar(max) NULL;
COMMIT;
