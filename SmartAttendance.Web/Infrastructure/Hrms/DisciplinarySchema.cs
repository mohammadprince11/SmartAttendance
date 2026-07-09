using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class DisciplinarySchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationCategories
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Description nvarchar(max) NULL,
        DisplayOrder int NOT NULL DEFAULT(10),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CategoryId int NOT NULL,
        Name nvarchar(250) NOT NULL,
        Description nvarchar(max) NULL,
        Severity nvarchar(40) NOT NULL DEFAULT('B'),
        ValidityMonths int NOT NULL DEFAULT(6),
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        IncludeInEvaluation bit NOT NULL DEFAULT(1),
        ShowToEmployee bit NOT NULL DEFAULT(1),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryPenaltyRules', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryPenaltyRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ViolationTypeId int NOT NULL,
        OccurrenceFrom int NOT NULL,
        OccurrenceTo int NOT NULL,
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        PenaltyAction nvarchar(250) NOT NULL,
        FinancialImpactType nvarchar(40) NOT NULL DEFAULT('None'),
        FinancialValue decimal(18,2) NOT NULL DEFAULT(0),
        ValidityMonths int NOT NULL DEFAULT(6),
        CalculationMode nvarchar(40) NOT NULL DEFAULT('Cumulative'),
        RequiresApproval bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryMessageTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryMessageTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        TemplateType nvarchar(60) NOT NULL DEFAULT('PenaltyNotice'),
        Subject nvarchar(250) NOT NULL,
        Body nvarchar(max) NOT NULL,
        IsDefault bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryTemplateTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryTemplateTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Code nvarchar(80) NOT NULL,
        Description nvarchar(max) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinarySettings', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinarySettings
    (
        [Key] nvarchar(120) NOT NULL PRIMARY KEY,
        [Value] nvarchar(max) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryFormTextBlocks', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryFormTextBlocks
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Area nvarchar(30) NOT NULL DEFAULT('Body'),
        Text nvarchar(max) NOT NULL,
        XPercent decimal(18,2) NOT NULL DEFAULT(8),
        YPercent decimal(18,2) NOT NULL DEFAULT(25),
        WidthPercent decimal(18,2) NOT NULL DEFAULT(84),
        FontFamily nvarchar(80) NOT NULL DEFAULT('Tahoma'),
        FontSize int NOT NULL DEFAULT(14),
        FontColor nvarchar(20) NOT NULL DEFAULT('#0b1d31'),
        IsBold bit NOT NULL DEFAULT(0),
        TextAlign nvarchar(20) NOT NULL DEFAULT('right'),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");
    }
}