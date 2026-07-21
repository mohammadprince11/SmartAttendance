using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the EmployeeAllowances table exists (runtime self-healing
/// schema path). Kayan-style "العلاوات" panel of the employee financial file.
/// </summary>
public static class EmployeeAllowanceSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeAllowances', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeAllowances
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ItemName nvarchar(150) NOT NULL,
        Amount decimal(18,4) NOT NULL DEFAULT(0),
        FromDate date NOT NULL,
        ToDate date NULL,
        EndAfterDate bit NOT NULL DEFAULT(0),
        Note nvarchar(500) NULL,
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );

    CREATE INDEX IX_EmployeeAllowances_EmployeeId ON EmployeeAllowances (EmployeeId);
END;
""");
    }
}
