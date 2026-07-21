using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the EmployeeContracts table exists (runtime self-healing
/// schema path). Kayan-style employee contracts: multiple rows per employee,
/// renewals are new rows.
/// </summary>
public static class EmployeeContractSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeContracts', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeContracts
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ContractNo nvarchar(50) NULL,
        ContractType nvarchar(100) NOT NULL,
        FromDate date NOT NULL,
        ToDate date NULL,
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

    CREATE INDEX IX_EmployeeContracts_EmployeeId ON EmployeeContracts (EmployeeId);
END;
""");
    }
}
