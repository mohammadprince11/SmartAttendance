using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeLifecycleSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            @"
IF COL_LENGTH('Employees', 'Position') IS NULL
BEGIN
    ALTER TABLE Employees ADD Position nvarchar(150) NULL;
END;

IF COL_LENGTH('Employees', 'EmploymentStatus') IS NULL
BEGIN
    ALTER TABLE Employees ADD EmploymentStatus nvarchar(80) NULL;
END;

IF COL_LENGTH('Employees', 'ServiceEndDate') IS NULL
BEGIN
    ALTER TABLE Employees ADD ServiceEndDate date NULL;
END;

IF COL_LENGTH('Employees', 'ServiceEndType') IS NULL
BEGIN
    ALTER TABLE Employees ADD ServiceEndType nvarchar(80) NULL;
END;

IF COL_LENGTH('Employees', 'ServiceEndReason') IS NULL
BEGIN
    ALTER TABLE Employees ADD ServiceEndReason nvarchar(1000) NULL;
END;

IF COL_LENGTH('Employees', 'ServiceEndNotes') IS NULL
BEGIN
    ALTER TABLE Employees ADD ServiceEndNotes nvarchar(2000) NULL;
END;

IF COL_LENGTH('Employees', 'ClearanceStatus') IS NULL
BEGIN
    ALTER TABLE Employees ADD ClearanceStatus nvarchar(80) NULL;
END;

IF COL_LENGTH('Employees', 'LastRehireDate') IS NULL
BEGIN
    ALTER TABLE Employees ADD LastRehireDate date NULL;
END;

IF COL_LENGTH('Employees', 'RehireReason') IS NULL
BEGIN
    ALTER TABLE Employees ADD RehireReason nvarchar(1000) NULL;
END;

IF COL_LENGTH('Employees', 'RehireNotes') IS NULL
BEGIN
    ALTER TABLE Employees ADD RehireNotes nvarchar(2000) NULL;
END;

IF COL_LENGTH('Employees', 'RehireCount') IS NULL
BEGIN
    ALTER TABLE Employees ADD RehireCount int NOT NULL CONSTRAINT DF_Employees_RehireCount DEFAULT(0);
END;

IF OBJECT_ID('EmployeeEndServices', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeEndServices
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        EmployeeNo nvarchar(80) NULL,
        EmployeeName nvarchar(250) NULL,
        EndServiceType nvarchar(80) NOT NULL,
        EndServiceTypeText nvarchar(150) NULL,
        LastWorkingDate date NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        HrNotes nvarchar(2000) NULL,
        ClearanceAssets bit NOT NULL DEFAULT(0),
        ClearanceDocuments bit NOT NULL DEFAULT(0),
        ClearanceAccommodation bit NOT NULL DEFAULT(0),
        ClearanceDevices bit NOT NULL DEFAULT(0),
        ClearanceBadge bit NOT NULL DEFAULT(0),
        ClearanceFinance bit NOT NULL DEFAULT(0),
        ClearanceStatus nvarchar(80) NULL,
        CreatedBy nvarchar(200) NULL,
        IpAddress nvarchar(80) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(GETDATE())
    );
END;

IF OBJECT_ID('EmployeeRehires', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeRehires
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        EmployeeNo nvarchar(80) NULL,
        EmployeeName nvarchar(250) NULL,
        PreviousHireDate date NULL,
        RehireDate date NOT NULL,
        PreviousEmploymentStatus nvarchar(80) NULL,
        Reason nvarchar(1000) NOT NULL,
        HrNotes nvarchar(2000) NULL,
        CreatedBy nvarchar(200) NULL,
        IpAddress nvarchar(80) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(GETDATE())
    );
END;

IF COL_LENGTH('EmployeeRehires', 'EmployeeNo') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD EmployeeNo nvarchar(80) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'EmployeeName') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD EmployeeName nvarchar(250) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'PreviousHireDate') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD PreviousHireDate date NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'PreviousEmploymentStatus') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD PreviousEmploymentStatus nvarchar(80) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'HrNotes') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD HrNotes nvarchar(2000) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD CreatedBy nvarchar(200) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'IpAddress') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD IpAddress nvarchar(80) NULL;
END;

IF COL_LENGTH('EmployeeRehires', 'CreatedAt') IS NULL
BEGIN
    ALTER TABLE EmployeeRehires ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeRehires_CreatedAt DEFAULT(GETDATE());
END;");
    }
}