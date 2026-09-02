SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CompanyLanguages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyLanguages
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyLanguages PRIMARY KEY,
        CompanyId int NOT NULL,
        CultureCode nvarchar(35) NOT NULL,
        NativeName nvarchar(120) NOT NULL,
        EnglishName nvarchar(120) NOT NULL,
        Direction nvarchar(3) NOT NULL,
        IsDefault bit NOT NULL,
        IsRequired bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NULL,
        IsDeleted bit NOT NULL,
        CreatedBy nvarchar(max) NULL,
        UpdatedBy nvarchar(max) NULL,
        CONSTRAINT FK_CompanyLanguages_Companies_CompanyId
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id) ON DELETE CASCADE,
        CONSTRAINT CK_CompanyLanguages_Direction CHECK (Direction IN ('rtl', 'ltr'))
    );

    CREATE UNIQUE INDEX UX_CompanyLanguages_Company_Culture
        ON dbo.CompanyLanguages(CompanyId, CultureCode);
    CREATE UNIQUE INDEX UX_CompanyLanguages_OneDefault
        ON dbo.CompanyLanguages(CompanyId)
        WHERE IsDefault = 1 AND IsActive = 1 AND IsDeleted = 0;
END;

IF OBJECT_ID(N'dbo.LocalizedEntityValues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizedEntityValues
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LocalizedEntityValues PRIMARY KEY,
        CompanyId int NOT NULL,
        EntityType nvarchar(80) NOT NULL,
        EntityId int NOT NULL,
        FieldName nvarchar(80) NOT NULL,
        CultureCode nvarchar(35) NOT NULL,
        Value nvarchar(4000) NOT NULL,
        TranslationStatus nvarchar(20) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NULL,
        IsDeleted bit NOT NULL,
        CreatedBy nvarchar(max) NULL,
        UpdatedBy nvarchar(max) NULL,
        CONSTRAINT FK_LocalizedEntityValues_Companies_CompanyId
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id) ON DELETE CASCADE,
        CONSTRAINT CK_LocalizedEntityValues_Status
            CHECK (TranslationStatus IN ('Manual', 'Machine', 'Reviewed'))
    );

    CREATE INDEX IX_LocalizedEntityValues_EntityCulture
        ON dbo.LocalizedEntityValues(CompanyId, EntityType, EntityId, CultureCode);
    CREATE UNIQUE INDEX UX_LocalizedEntityValues_FieldCulture
        ON dbo.LocalizedEntityValues(CompanyId, EntityType, EntityId, FieldName, CultureCode);
END;

COMMIT TRANSACTION;
