using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Persists optional deductions that exceed the configured monthly cap.
/// Recalculation is idempotent: effects created/applied by the recalculated run
/// are reset first, then the newly deferred balance is stored again.
/// </summary>
public static class PayrollDeferredDeductionStore
{
    public static Task EnsureAsync(ApplicationDbContext db) => HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('PayrollDeferredDeductions','U') IS NULL
BEGIN
    CREATE TABLE PayrollDeferredDeductions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceRunId int NOT NULL,
        EmployeeId int NOT NULL,
        Amount decimal(18,2) NOT NULL,
        RemainingAmount decimal(18,2) NOT NULL,
        AppliedRunId int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        AppliedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_PayrollDeferredDeductions_SourceEmployee
        ON PayrollDeferredDeductions(SourceRunId, EmployeeId);
    CREATE INDEX IX_PayrollDeferredDeductions_EmployeeOpen
        ON PayrollDeferredDeductions(EmployeeId, AppliedRunId)
        INCLUDE(RemainingAmount, SourceRunId);
END;
""");

    /// <summary>
    /// Removes a run's newly-created deferred balance and releases any older
    /// balances that this run had claimed. Call before recalculating/deleting.
    /// </summary>
    public static async Task ResetRunAsync(ApplicationDbContext db, int runId)
    {
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, """
DELETE FROM PayrollDeferredDeductions WHERE SourceRunId=@Run;
UPDATE PayrollDeferredDeductions
SET AppliedRunId=NULL, AppliedAt=NULL
WHERE AppliedRunId=@Run;
""", command => HrmsDatabase.AddParameter(command, "@Run", runId));
    }

    public static async Task<decimal> CarryInAsync(
        ApplicationDbContext db, int employeeId, int runId)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<decimal>(db, """
SELECT ISNULL(SUM(d.RemainingAmount),0)
FROM PayrollDeferredDeductions d
INNER JOIN PayrollRuns sourceRun ON sourceRun.Id=d.SourceRunId
LEFT JOIN PayrollRuns appliedRun ON appliedRun.Id=d.AppliedRunId
WHERE d.EmployeeId=@Emp AND d.SourceRunId<>@Run
  AND sourceRun.Status IN (N'Locked',N'Issued',N'PayslipSent')
  AND (d.AppliedRunId IS NULL OR appliedRun.Status IN (N'Draft',N'Calculated'));
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Emp", employeeId);
            HrmsDatabase.AddParameter(command, "@Run", runId);
        });
    }

    /// <summary>
    /// Claims previous balances for this run and stores the amount that remains
    /// deferred after applying the current run's cap.
    /// </summary>
    public static async Task RecordResultAsync(
        ApplicationDbContext db, int runId, int employeeId, decimal carryIn, decimal deferred)
    {
        await EnsureAsync(db);
        if (carryIn > 0m)
        {
            await HrmsDatabase.ExecuteAsync(db, """
UPDATE d SET AppliedRunId=@Run, AppliedAt=SYSUTCDATETIME()
FROM PayrollDeferredDeductions d
INNER JOIN PayrollRuns sourceRun ON sourceRun.Id=d.SourceRunId
LEFT JOIN PayrollRuns appliedRun ON appliedRun.Id=d.AppliedRunId
WHERE d.EmployeeId=@Emp AND d.SourceRunId<>@Run
  AND sourceRun.Status IN (N'Locked',N'Issued',N'PayslipSent')
  AND (d.AppliedRunId IS NULL OR appliedRun.Status IN (N'Draft',N'Calculated'));
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@Run", runId);
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
            });
        }

        await HrmsDatabase.ExecuteAsync(db,
            "DELETE FROM PayrollDeferredDeductions WHERE SourceRunId=@Run AND EmployeeId=@Emp;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Run", runId);
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
            });

        if (deferred <= 0m) return;

        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO PayrollDeferredDeductions(SourceRunId,EmployeeId,Amount,RemainingAmount)
VALUES(@Run,@Emp,@Amount,@Amount);
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Run", runId);
            HrmsDatabase.AddParameter(command, "@Emp", employeeId);
            HrmsDatabase.AddParameter(command, "@Amount", decimal.Round(deferred, 2));
        });
    }
}
