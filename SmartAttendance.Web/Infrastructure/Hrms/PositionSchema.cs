using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PositionSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            @"
IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositions PRIMARY KEY,
        ArabicName NVARCHAR(400) NOT NULL,
        EnglishName NVARCHAR(400) NULL,
        JobCode NVARCHAR(160) NULL,
        DepartmentId INT NULL,
        Grade NVARCHAR(160) NULL,
        Category NVARCHAR(160) NULL,
        Level NVARCHAR(160) NULL,
        Description NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCategories
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCategories PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCategories_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCategories_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionLevels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionLevels
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionLevels PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionLevels_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionLevels_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF COL_LENGTH(N'dbo.HrJobPositions', N'ArabicName') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD ArabicName NVARCHAR(400) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'EnglishName') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD EnglishName NVARCHAR(400) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'JobCode') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobCode NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'DepartmentId') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD DepartmentId INT NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Grade') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Grade NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Category') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Category NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Level') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Level NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Description') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Description NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'IsActive') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD IsActive BIT NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'CreatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD CreatedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD UpdatedAt DATETIME2 NULL;

IF COL_LENGTH(N'dbo.HrJobPositionCategories', N'Name') IS NULL
    ALTER TABLE dbo.HrJobPositionCategories ADD Name NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.HrJobPositionCategories', N'IsActive') IS NULL
    ALTER TABLE dbo.HrJobPositionCategories ADD IsActive BIT NULL;
IF COL_LENGTH(N'dbo.HrJobPositionCategories', N'CreatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositionCategories ADD CreatedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.HrJobPositionCategories', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositionCategories ADD UpdatedAt DATETIME2 NULL;

IF COL_LENGTH(N'dbo.HrJobPositionLevels', N'Name') IS NULL
    ALTER TABLE dbo.HrJobPositionLevels ADD Name NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.HrJobPositionLevels', N'IsActive') IS NULL
    ALTER TABLE dbo.HrJobPositionLevels ADD IsActive BIT NULL;
IF COL_LENGTH(N'dbo.HrJobPositionLevels', N'CreatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositionLevels ADD CreatedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.HrJobPositionLevels', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositionLevels ADD UpdatedAt DATETIME2 NULL;

UPDATE dbo.HrJobPositions SET IsActive = 1 WHERE IsActive IS NULL;
UPDATE dbo.HrJobPositions SET CreatedAt = SYSDATETIME() WHERE CreatedAt IS NULL;

UPDATE dbo.HrJobPositionCategories SET IsActive = 1 WHERE IsActive IS NULL;
UPDATE dbo.HrJobPositionCategories SET CreatedAt = SYSDATETIME() WHERE CreatedAt IS NULL;

UPDATE dbo.HrJobPositionLevels SET IsActive = 1 WHERE IsActive IS NULL;
UPDATE dbo.HrJobPositionLevels SET CreatedAt = SYSDATETIME() WHERE CreatedAt IS NULL;
");
    }
}