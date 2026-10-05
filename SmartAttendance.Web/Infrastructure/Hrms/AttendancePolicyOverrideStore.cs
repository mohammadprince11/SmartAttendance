using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class AttendancePolicyOverrideStore
{
    public const string MonthlyLateAllowancePolicyKey = "MonthlyLateAllowance";
    public const string AbsentStatus = "Absent";

    public sealed record OverrideRow(
        int EmployeeId,
        DateOnly WorkDate,
        string OverrideStatus,
        string Reason);

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('AttendancePolicyOverrides', 'U') IS NULL
BEGIN
    CREATE TABLE AttendancePolicyOverrides
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        PolicyKey nvarchar(80) NOT NULL,
        OverrideStatus nvarchar(20) NOT NULL,
        Reason nvarchar(300) NOT NULL DEFAULT(N''),
        GeneratedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_AttendancePolicyOverrides_EmployeeDatePolicy
        ON AttendancePolicyOverrides (EmployeeId, WorkDate, PolicyKey);
    CREATE INDEX IX_AttendancePolicyOverrides_WorkDate
        ON AttendancePolicyOverrides (WorkDate, EmployeeId);
END;
""");
    }

    public static IReadOnlyList<OverrideRow> BuildLateAllowanceOverrides(
        IEnumerable<DayAttendanceStore.DayRow> days,
        AttendanceLatenessPolicy.Policy policy)
    {
        ArgumentNullException.ThrowIfNull(days);
        var normalized = policy.Normalized();
        if (!normalized.Enabled || !normalized.ExceededAsAbsent)
            return Array.Empty<OverrideRow>();

        var result = new List<OverrideRow>();
        foreach (var employeeDays in days
                     .Where(day => DayAttendanceStore.IsWorkingKind(day.DayKind)
                                   && day.LateHours > 0
                                   && day.Status is "Late" or "Present")
                     .GroupBy(day => day.EmployeeId))
        {
            var lateDays = employeeDays.Select(day =>
                new AttendanceLateAllowancePolicy.LateDay(
                    day.WorkDate,
                    Math.Max(0, (int)Math.Round(day.LateHours * 60m, MidpointRounding.AwayFromZero))));

            var evaluations = AttendanceLatenessPolicy.Evaluate(lateDays, normalized);
            foreach (var evaluation in evaluations.Where(row => row.IsViolationDay))
            {
                result.Add(new OverrideRow(
                    employeeDays.Key,
                    evaluation.Date,
                    AbsentStatus,
                    $"تجاوز سماح التأخير الشهري ({normalized.AllowanceMinutes} دقيقة)"));
            }
        }

        return result;
    }

    public static async Task<int> RebuildLateAllowanceAsync(
        ApplicationDbContext db,
        CompanyScope scope,
        int year,
        int month,
        int companyId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (companyId <= 0 || !scope.Allows(companyId)) return 0;

        await EnsureAsync(db);
        var policy = await AttendanceLatenessPolicy.LoadAsync(db, companyId);
        var (period, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(
            db, year, month, PayrollCutoffType.Attendance, companyId);

        await using var transaction = await db.Database.BeginTransactionAsync();

        await HrmsDatabase.ExecuteAsync(db, """
DELETE po
FROM AttendancePolicyOverrides po
INNER JOIN Employees e ON e.Id = po.EmployeeId
WHERE po.PolicyKey = @Policy
  AND e.CompanyId = @Company
  AND po.WorkDate >= @From AND po.WorkDate <= @To;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Policy", MonthlyLateAllowancePolicyKey);
            HrmsDatabase.AddParameter(command, "@Company", companyId);
            HrmsDatabase.AddParameter(command, "@From", period.From.ToDateTime(TimeOnly.MinValue));
            HrmsDatabase.AddParameter(command, "@To", period.To.ToDateTime(TimeOnly.MinValue));
        });

        if (!policy.Enabled || !policy.ExceededAsAbsent)
        {
            await transaction.CommitAsync();
            return 0;
        }

        var companyScope = CompanyScope.ForCompanies(new[] { companyId });
        var days = await DayAttendanceStore.ListRangeAsync(
            db, companyScope, period.From, period.To, null, computeStale: false);
        var overrides = BuildLateAllowanceOverrides(days, policy);

        foreach (var row in overrides)
        {
            await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO AttendancePolicyOverrides
    (EmployeeId, WorkDate, PolicyKey, OverrideStatus, Reason, GeneratedAt)
VALUES
    (@Employee, @Date, @Policy, @Status, @Reason, SYSUTCDATETIME());
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", row.EmployeeId);
                HrmsDatabase.AddParameter(command, "@Date", row.WorkDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Policy", MonthlyLateAllowancePolicyKey);
                HrmsDatabase.AddParameter(command, "@Status", row.OverrideStatus);
                HrmsDatabase.AddParameter(command, "@Reason", row.Reason);
            });
        }

        await transaction.CommitAsync();
        return overrides.Count;
    }
}
