using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the EmployeeDependents table exists (runtime self-healing
/// schema path, same as the other HRMS schema classes). First panel of the 360°
/// employee file.
/// </summary>
public static class EmployeeDependentSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeDependents', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeDependents
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Relation int NOT NULL,
        Name nvarchar(200) NOT NULL,
        NameOther nvarchar(200) NULL,
        BirthDate date NULL,
        MarriageDate date NULL,
        Religion nvarchar(80) NULL,
        Nationality nvarchar(100) NULL,
        NationalId nvarchar(50) NULL,
        PassportNo nvarchar(50) NULL,
        IsCitizen bit NOT NULL DEFAULT(0),
        MaritalStatus nvarchar(50) NULL,
        IsEmergencyContact bit NOT NULL DEFAULT(0),
        IsSpecialNeeds bit NOT NULL DEFAULT(0),
        IsWorking bit NOT NULL DEFAULT(0),
        IsDependent bit NOT NULL DEFAULT(0),
        MobilePhone nvarchar(50) NULL,
        CompanyName nvarchar(200) NULL,
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );

    CREATE INDEX IX_EmployeeDependents_EmployeeId ON EmployeeDependents (EmployeeId);
END;
""");
    }
}
