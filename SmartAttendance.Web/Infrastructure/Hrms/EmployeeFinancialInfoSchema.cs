using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the EmployeeFinancialInfos table exists (runtime self-healing
/// schema path, same as the other HRMS schema classes). One row per employee — the
/// Kayan "المعلومات المالية" form; bridge to the payroll module.
/// </summary>
public static class EmployeeFinancialInfoSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeFinancialInfos', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeFinancialInfos
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,

        Currency nvarchar(10) NULL,
        SalaryScale nvarchar(150) NULL,
        BasicSalary decimal(18,4) NULL,
        DailySalary decimal(18,4) NULL,
        HourlyRate decimal(18,4) NULL,

        SocialSecurityType nvarchar(100) NULL,
        SocialSecuritySalary decimal(18,4) NULL,
        SocialSecurityNo nvarchar(50) NULL,
        SocialSecurityJoinDate date NULL,
        SocialSecurityPreviousMonths int NULL,
        RetirementAge int NULL,

        TaxFile nvarchar(150) NULL,
        TaxNo nvarchar(50) NULL,
        TaxYear int NULL,
        PreviousTaxSalary decimal(18,4) NULL,
        PreviousTaxExemption decimal(18,4) NULL,
        PreviousTaxAmount decimal(18,4) NULL,
        PreviousMinSalary decimal(18,4) NULL,
        PreviousMinTaxAmount decimal(18,4) NULL,
        PreviousTaxMonths int NULL,

        EndOfServiceSetup nvarchar(150) NULL,
        EndOfServiceCompute bit NOT NULL DEFAULT(0),
        EndOfServiceStartDate date NULL,
        EndOfServiceDueDate date NULL,

        CalcPreviousSalaries bit NOT NULL DEFAULT(0),
        StopSalaryCalc bit NOT NULL DEFAULT(0),
        AdditionalSalaryStartDate date NULL,

        PaymentMethod nvarchar(50) NULL,
        BankName nvarchar(200) NULL,
        BankBranch nvarchar(200) NULL,
        UnitNo nvarchar(50) NULL,
        Iban nvarchar(50) NULL,
        CardNo nvarchar(50) NULL,
        MxpAccount nvarchar(50) NULL,
        BankCommitment bit NOT NULL DEFAULT(0),
        BankCommitmentFileName nvarchar(260) NULL,
        BankCommitmentFilePath nvarchar(500) NULL,
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(500) NULL,

        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );

    CREATE UNIQUE INDEX UX_EmployeeFinancialInfos_EmployeeId ON EmployeeFinancialInfos (EmployeeId);
END;
""");
    }
}
