using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class ViolationCaseSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeViolationCases', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeViolationCases
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo nvarchar(50) NOT NULL,
        EmployeeId int NOT NULL,
        ViolationTypeId int NULL,
        PenaltyRuleId int NULL,
        ViolationCategory nvarchar(150) NOT NULL DEFAULT(N''),
        ViolationTitle nvarchar(500) NOT NULL DEFAULT(N''),
        EventDate date NOT NULL,
        Source nvarchar(50) NOT NULL DEFAULT((NCHAR(1605)+NCHAR(1576)+NCHAR(1575)+NCHAR(1588)+NCHAR(1585))),
        ActionStatus nvarchar(100) NOT NULL DEFAULT((NCHAR(1576)+NCHAR(1575)+NCHAR(1606)+NCHAR(1578)+NCHAR(1592)+NCHAR(1575)+NCHAR(1585)+NCHAR(32)+NCHAR(1575)+NCHAR(1604)+NCHAR(1573)+NCHAR(1580)+NCHAR(1585)+NCHAR(1575)+NCHAR(1569))),
        Status nvarchar(100) NOT NULL DEFAULT((NCHAR(1605)+NCHAR(1587)+NCHAR(1608)+NCHAR(1583)+NCHAR(1577))),
        ProposedAction nvarchar(300) NULL,
        FinalPenaltyAction nvarchar(300) NULL,
        FinancialImpactType nvarchar(40) NOT NULL DEFAULT(N'None'),
        FinancialImpactValue decimal(18,2) NOT NULL DEFAULT(0),
        DeductionAmount decimal(18,2) NOT NULL DEFAULT(0),
        Notes nvarchar(1000) NULL,
        FinalAction nvarchar(300) NULL,
        ApprovedAt datetime2 NULL,
        ClosedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
END;

IF COL_LENGTH('EmployeeViolationCases', 'ViolationTypeId') IS NULL
    ALTER TABLE EmployeeViolationCases ADD ViolationTypeId int NULL;

IF COL_LENGTH('EmployeeViolationCases', 'PenaltyRuleId') IS NULL
    ALTER TABLE EmployeeViolationCases ADD PenaltyRuleId int NULL;

IF COL_LENGTH('EmployeeViolationCases', 'FinalPenaltyAction') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinalPenaltyAction nvarchar(300) NULL;

IF COL_LENGTH('EmployeeViolationCases', 'FinancialImpactType') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinancialImpactType nvarchar(40) NOT NULL CONSTRAINT DF_EmployeeViolationCases_FinancialImpactType DEFAULT(N'None');

IF COL_LENGTH('EmployeeViolationCases', 'FinancialImpactValue') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinancialImpactValue decimal(18,2) NOT NULL CONSTRAINT DF_EmployeeViolationCases_FinancialImpactValue DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'DeductionAmount') IS NULL
    ALTER TABLE EmployeeViolationCases ADD DeductionAmount decimal(18,2) NOT NULL CONSTRAINT DF_EmployeeViolationCases_DeductionAmount DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'IsDeleted') IS NULL
    ALTER TABLE EmployeeViolationCases ADD IsDeleted bit NOT NULL CONSTRAINT DF_EmployeeViolationCases_IsDeleted DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'UpdatedAt') IS NULL
    ALTER TABLE EmployeeViolationCases ADD UpdatedAt datetime2 NULL;
""");
    }
}