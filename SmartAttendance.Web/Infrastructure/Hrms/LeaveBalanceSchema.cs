using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the LeaveBalances table exists. This project self-heals its
/// schema at runtime (no startup migrations), mirroring the other HRMS schema classes.
/// </summary>
public static class LeaveBalanceSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('LeaveBalances', 'U') IS NULL
BEGIN
    CREATE TABLE LeaveBalances
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Year int NOT NULL,
        LeaveType int NOT NULL,
        EntitledDays decimal(5,1) NOT NULL DEFAULT(0),
        CarriedOverDays decimal(5,1) NOT NULL DEFAULT(0),
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );

    CREATE UNIQUE INDEX IX_LeaveBalances_Employee_Year_Type
        ON LeaveBalances (EmployeeId, Year, LeaveType);
END;
""");
    }
}
