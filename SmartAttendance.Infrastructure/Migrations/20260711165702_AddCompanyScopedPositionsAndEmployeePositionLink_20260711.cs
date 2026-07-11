using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyScopedPositionsAndEmployeePositionLink_20260711 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PositionId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_PositionId",
                table: "Employees",
                column: "PositionId");
            migrationBuilder.Sql(@"IF OBJECT_ID(N'dbo.HrJobPositionCategories', N'U') IS NULL
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

IF OBJECT_ID(N'dbo.HrJobPositionCompetencyOptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCompetencyOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCompetencyOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCompetencyOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCompetencyOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionEducationOptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionEducationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionEducationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionEducationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionEducationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionEducationSpecializationOptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionEducationSpecializationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionEducationSpecializationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionEducationSpecializationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionEducationSpecializationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionCertificationOptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCertificationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCertificationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCertificationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCertificationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositions PRIMARY KEY,
        CompanyId INT NULL,
        ArabicName NVARCHAR(400) NOT NULL,
        EnglishName NVARCHAR(400) NULL,
        JobCode NVARCHAR(160) NULL,
        DepartmentId INT NULL,
        Grade NVARCHAR(160) NULL,
        Category NVARCHAR(160) NULL,
        Level NVARCHAR(160) NULL,
        Description NVARCHAR(MAX) NULL,
        JobPurpose NVARCHAR(MAX) NULL,
        KeyResponsibilities NVARCHAR(MAX) NULL,
        JobRequirements NVARCHAR(MAX) NULL,
        RequiredSkills NVARCHAR(MAX) NULL,
        JobKpis NVARCHAR(MAX) NULL,
        Competencies NVARCHAR(MAX) NULL,
        Education NVARCHAR(MAX) NULL,
        EducationSpecialization NVARCHAR(MAX) NULL,
        Certifications NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF COL_LENGTH(N'dbo.HrJobPositions', N'CompanyId') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD CompanyId INT NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'JobPurpose') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobPurpose NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'KeyResponsibilities') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD KeyResponsibilities NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'JobRequirements') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobRequirements NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'RequiredSkills') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD RequiredSkills NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'JobKpis') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobKpis NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Competencies') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Competencies NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Education') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Education NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'EducationSpecialization') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD EducationSpecialization NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Certifications') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Certifications NVARCHAR(MAX) NULL;

");

            migrationBuilder.Sql(@"UPDATE position
SET CompanyId = department.CompanyId
FROM dbo.HrJobPositions position
INNER JOIN dbo.Departments department
    ON department.Id = position.DepartmentId
WHERE position.CompanyId IS NULL;

DECLARE @activeCompanyCount INT =
(
    SELECT COUNT(*)
    FROM dbo.Companies
    WHERE IsDeleted = 0
);

DECLARE @singleCompanyId INT =
(
    SELECT MIN(Id)
    FROM dbo.Companies
    WHERE IsDeleted = 0
);

IF @activeCompanyCount = 1
BEGIN
    UPDATE dbo.HrJobPositions
    SET CompanyId = @singleCompanyId
    WHERE CompanyId IS NULL;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.HrJobPositions
    WHERE CompanyId IS NULL
)
BEGIN
    THROW 51000, 'Existing positions could not be assigned to a company.', 1;
END;

ALTER TABLE dbo.HrJobPositions
ALTER COLUMN CompanyId INT NOT NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HrJobPositions')
      AND name = N'IX_HrJobPositions_CompanyId'
)
BEGIN
    CREATE INDEX IX_HrJobPositions_CompanyId
        ON dbo.HrJobPositions (CompanyId);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HrJobPositions')
      AND name = N'UX_HrJobPositions_CompanyId_ArabicName'
)
BEGIN
    CREATE UNIQUE INDEX UX_HrJobPositions_CompanyId_ArabicName
        ON dbo.HrJobPositions (CompanyId, ArabicName);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_HrJobPositions_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.HrJobPositions
    ADD CONSTRAINT FK_HrJobPositions_Companies_CompanyId
        FOREIGN KEY (CompanyId)
        REFERENCES dbo.Companies (Id);
END;

UPDATE employee
SET PositionId = position.Id,
    Position = position.ArabicName
FROM dbo.Employees employee
INNER JOIN dbo.Branches branch
    ON branch.Id = employee.BranchId
INNER JOIN dbo.HrJobPositions position
    ON position.CompanyId = branch.CompanyId
   AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), employee.Position))) =
       LTRIM(RTRIM(position.ArabicName))
WHERE employee.Position IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), employee.Position))) <> N'';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Employees_HrJobPositions_PositionId'
)
BEGIN
    ALTER TABLE dbo.Employees
    ADD CONSTRAINT FK_Employees_HrJobPositions_PositionId
        FOREIGN KEY (PositionId)
        REFERENCES dbo.HrJobPositions (Id);
END;

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_Employees_SyncPositionId
ON dbo.Employees
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF TRIGGER_NESTLEVEL() > 1
        RETURN;

    UPDATE employee
    SET PositionId = matchedPosition.Id
    FROM dbo.Employees employee
    INNER JOIN inserted source
        ON source.Id = employee.Id
    INNER JOIN dbo.Branches branch
        ON branch.Id = source.BranchId
    OUTER APPLY
    (
        SELECT TOP (1) position.Id
        FROM dbo.HrJobPositions position
        WHERE position.CompanyId = branch.CompanyId
          AND position.IsActive = 1
          AND LTRIM(RTRIM(position.ArabicName)) =
              LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), source.Position)))
        ORDER BY position.Id
    ) matchedPosition
    WHERE employee.Id = source.Id;
END;
');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF OBJECT_ID(N'dbo.TR_Employees_SyncPositionId', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_Employees_SyncPositionId;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Employees_HrJobPositions_PositionId'
)
BEGIN
    ALTER TABLE dbo.Employees
    DROP CONSTRAINT FK_Employees_HrJobPositions_PositionId;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_HrJobPositions_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.HrJobPositions
    DROP CONSTRAINT FK_HrJobPositions_Companies_CompanyId;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HrJobPositions')
      AND name = N'UX_HrJobPositions_CompanyId_ArabicName'
)
BEGIN
    DROP INDEX UX_HrJobPositions_CompanyId_ArabicName
    ON dbo.HrJobPositions;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HrJobPositions')
      AND name = N'IX_HrJobPositions_CompanyId'
)
BEGIN
    DROP INDEX IX_HrJobPositions_CompanyId
    ON dbo.HrJobPositions;
END;

IF COL_LENGTH(N'dbo.HrJobPositions', N'CompanyId') IS NOT NULL
    ALTER TABLE dbo.HrJobPositions DROP COLUMN CompanyId;");

            migrationBuilder.DropIndex(
                name: "IX_Employees_PositionId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PositionId",
                table: "Employees");
        }
    }
}
