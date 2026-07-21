using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the structured 360° records table exists. Runtime
/// self-healing schema path, same as the other HRMS schema classes.
/// </summary>
public static class EmployeeRecordsSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeFileRecords', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeFileRecords
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        RecordType int NOT NULL,
        Title nvarchar(200) NOT NULL,
        Subtitle nvarchar(200) NULL,
        Country nvarchar(100) NULL,
        RefNo nvarchar(100) NULL,
        FromDate date NULL,
        ToDate date NULL,
        Amount decimal(18,2) NULL,
        IsCurrent bit NOT NULL DEFAULT(0),
        Note nvarchar(500) NULL,
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_EmployeeFileRecords_Employee_Type ON EmployeeFileRecords (EmployeeId, RecordType);
END;

IF COL_LENGTH('EmployeeFileRecords', 'Amount') IS NULL
    ALTER TABLE EmployeeFileRecords ADD Amount decimal(18,2) NULL;

IF COL_LENGTH('EmployeeFileRecords', 'IsCurrent') IS NULL
    ALTER TABLE EmployeeFileRecords ADD IsCurrent bit NOT NULL CONSTRAINT DF_EmployeeFileRecords_IsCurrent DEFAULT(0);

IF COL_LENGTH('EmployeeFileRecords', 'IsReturned') IS NULL
    ALTER TABLE EmployeeFileRecords ADD IsReturned bit NOT NULL CONSTRAINT DF_EmployeeFileRecords_IsReturned DEFAULT(0);

IF COL_LENGTH('EmployeeFileRecords', 'ReturnDate') IS NULL
    ALTER TABLE EmployeeFileRecords ADD ReturnDate date NULL;

IF COL_LENGTH('EmployeeFileRecords', 'Gpa') IS NULL
    ALTER TABLE EmployeeFileRecords ADD Gpa nvarchar(20) NULL;

IF COL_LENGTH('EmployeeFileRecords', 'RefContactName') IS NULL
    ALTER TABLE EmployeeFileRecords ADD RefContactName nvarchar(200) NULL;

IF COL_LENGTH('EmployeeFileRecords', 'RefContactPosition') IS NULL
    ALTER TABLE EmployeeFileRecords ADD RefContactPosition nvarchar(200) NULL;

IF COL_LENGTH('EmployeeFileRecords', 'RefContactPhone') IS NULL
    ALTER TABLE EmployeeFileRecords ADD RefContactPhone nvarchar(50) NULL;

IF COL_LENGTH('EmployeeFileRecords', 'RefContactNote') IS NULL
    ALTER TABLE EmployeeFileRecords ADD RefContactNote nvarchar(500) NULL;
""");
    }
}
