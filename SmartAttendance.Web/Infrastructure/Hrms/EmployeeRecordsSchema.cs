using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the 360° file record tables exist (education, experience,
/// certificates). Runtime self-healing schema path, same as the other HRMS schema
/// classes.
/// </summary>
public static class EmployeeRecordsSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeEducations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeEducations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Country nvarchar(100) NULL,
        University nvarchar(200) NOT NULL,
        Degree nvarchar(120) NULL,
        Major nvarchar(150) NULL,
        FromDate date NULL,
        ToDate date NULL,
        IsLatest bit NOT NULL DEFAULT(0),
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_EmployeeEducations_EmployeeId ON EmployeeEducations (EmployeeId);
END;

IF OBJECT_ID('EmployeeExperiences', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeExperiences
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        CompanyName nvarchar(200) NOT NULL,
        Country nvarchar(100) NULL,
        JobTitle nvarchar(150) NULL,
        FromDate date NULL,
        ToDate date NULL,
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_EmployeeExperiences_EmployeeId ON EmployeeExperiences (EmployeeId);
END;

IF OBJECT_ID('EmployeeCertificates', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCertificates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Name nvarchar(200) NOT NULL,
        ReferenceNo nvarchar(100) NULL,
        IssueDate date NULL,
        FromDate date NULL,
        ToDate date NULL,
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_EmployeeCertificates_EmployeeId ON EmployeeCertificates (EmployeeId);
END;
""");
    }
}
