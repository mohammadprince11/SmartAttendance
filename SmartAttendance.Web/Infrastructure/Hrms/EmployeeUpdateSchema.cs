using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeUpdateSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeUpdateBatches', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeUpdateBatches
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        SectionKey nvarchar(80) NOT NULL,
        SectionName nvarchar(150) NOT NULL,
        Status nvarchar(40) NOT NULL DEFAULT('Open'),
        RequestedBy nvarchar(150) NULL,
        RequestedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        LockedBy nvarchar(150) NULL,
        LockedAt datetime2 NULL,
        Note nvarchar(max) NULL
    );
END;

IF OBJECT_ID('EmployeeUpdateChanges', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeUpdateChanges
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BatchId int NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        FieldLabel nvarchar(150) NOT NULL,
        OldValue nvarchar(max) NULL,
        NewValue nvarchar(max) NULL
    );
END;

IF OBJECT_ID('EmployeeCustomFields', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCustomFields
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        FieldLabel nvarchar(150) NULL,
        FieldValue nvarchar(max) NULL,
        UpdatedAt datetime2 NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmployeeCustomFields_Employee_Field' AND object_id = OBJECT_ID('EmployeeCustomFields'))
BEGIN
    CREATE UNIQUE INDEX UX_EmployeeCustomFields_Employee_Field ON EmployeeCustomFields(EmployeeId, FieldKey);
END;

IF OBJECT_ID('EmployeeCompensations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCompensations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NULL,
        Allowances decimal(18,2) NULL,
        Deductions decimal(18,2) NULL,
        PaymentMethod nvarchar(80) NULL,
        BankName nvarchar(150) NULL,
        BankAccount nvarchar(150) NULL,
        Currency nvarchar(30) NULL,
        UpdatedAt datetime2 NULL
    );
END;
""");
    }
}