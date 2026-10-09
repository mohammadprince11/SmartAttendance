namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Reviewed export of the EF announcement-only migration for the canonical deployment ledger.</summary>
public static class AnnouncementStudioSchema
{
    public const string MigrationId = "20261009-01-dynamic-announcement-templates";
    public const string Sql = """
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID('AnnouncementContents','U') IS NULL OR OBJECT_ID('Companies','U') IS NULL
    THROW 51000, 'Announcement foundation must be installed before the studio migration.', 1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('AnnouncementContents') AND name='CK_AnnouncementContents_LanguageCode')
    ALTER TABLE AnnouncementContents DROP CONSTRAINT CK_AnnouncementContents_LanguageCode;
IF COL_LENGTH('AnnouncementContents','LanguageCode') < 70
BEGIN
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('AnnouncementContents') AND name='IX_AnnouncementContents_AnnouncementGroupId_LanguageCode')
        DROP INDEX IX_AnnouncementContents_AnnouncementGroupId_LanguageCode ON AnnouncementContents;
    ALTER TABLE AnnouncementContents ALTER COLUMN LanguageCode nvarchar(35) NOT NULL;
    CREATE UNIQUE INDEX IX_AnnouncementContents_AnnouncementGroupId_LanguageCode ON AnnouncementContents(AnnouncementGroupId,LanguageCode);
END;
IF COL_LENGTH('AnnouncementContents','PresentationJson') IS NULL
    ALTER TABLE AnnouncementContents ADD PresentationJson nvarchar(2000) NULL;
IF OBJECT_ID('AnnouncementStudioDesigns','U') IS NULL
BEGIN
    CREATE TABLE AnnouncementStudioDesigns (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL REFERENCES Companies(Id),
        Name nvarchar(150) NOT NULL,
        ContentType nvarchar(30) NOT NULL,
        Data varbinary(max) NOT NULL,
        IsActive bit NOT NULL
    );
    CREATE INDEX IX_AnnouncementStudioDesigns_CompanyId_IsActive ON AnnouncementStudioDesigns(CompanyId,IsActive);
END;
IF OBJECT_ID('AnnouncementStudioProfiles','U') IS NULL
BEGIN
    CREATE TABLE AnnouncementStudioProfiles (
        Id int IDENTITY NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL REFERENCES Companies(Id),
        [Key] nvarchar(80) NOT NULL,
        DefinitionJson nvarchar(max) NOT NULL,
        IsActive bit NOT NULL,
        Revision uniqueidentifier NOT NULL
    );
    CREATE UNIQUE INDEX IX_AnnouncementStudioProfiles_CompanyId_Key ON AnnouncementStudioProfiles(CompanyId,[Key]);
END;
COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
""";
}
