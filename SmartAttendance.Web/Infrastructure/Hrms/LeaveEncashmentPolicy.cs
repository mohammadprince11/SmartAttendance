using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Server-side guard for annual-leave encashment. The UI balance is informative;
/// this policy is the financial enforcement point and also subtracts previous
/// non-rejected encashment transactions so the same leave days cannot be paid twice.
/// </summary>
public static class LeaveEncashmentPolicy
{
    public static async Task<decimal> AvailableAnnualDaysAsync(
        ApplicationDbContext db,
        CompanyScope scope,
        int employeeId,
        int year,
        int excludeTransactionId = 0)
    {
        if (employeeId <= 0 || year < 2000 || scope.IsDeniedAll) return 0m;

        var allowed = await HrmsDatabase.ScalarAsync<int>(db,
            $"SELECT COUNT(*) FROM Employees e WHERE e.Id=@Emp AND ISNULL(e.IsDeleted,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Emp", employeeId));
        if (allowed != 1) return 0m;

        var balances = await LeaveBalanceCalculator.ForEmployeeAsync(db, employeeId, year);
        var annual = balances.FirstOrDefault(x => x.Type == LeaveType.Annual)?.Remaining ?? 0m;

        await PayrollTransactionStore.EnsureAsync(db);
        var alreadyEncashed = await HrmsDatabase.ScalarAsync<decimal>(db, """
SELECT ISNULL(SUM(ABS(ISNULL(Days,0))),0)
FROM PayrollTransactions
WHERE EmployeeId=@Emp AND [Year]=@Year AND TxType=N'LeaveEncashment'
  AND ISNULL(Status,N'Approved')<>N'Rejected'
  AND Id<>@Exclude;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Emp", employeeId);
            HrmsDatabase.AddParameter(command, "@Year", year);
            HrmsDatabase.AddParameter(command, "@Exclude", excludeTransactionId);
        });

        return Math.Max(0m, decimal.Round(annual - alreadyEncashed, 2));
    }

    public static async Task<(bool Ok, decimal Available, string? Error)> ValidateAsync(
        ApplicationDbContext db,
        CompanyScope scope,
        int employeeId,
        int year,
        decimal requestedDays,
        int excludeTransactionId = 0)
    {
        if (requestedDays <= 0m)
            return (false, 0m, "عدد أيام بدل الإجازة يجب أن يكون أكبر من صفر.");

        var available = await AvailableAnnualDaysAsync(
            db, scope, employeeId, year, excludeTransactionId);

        if (requestedDays > available)
        {
            return (false, available,
                $"لا يمكن صرف {requestedDays:0.##} يوم. الرصيد السنوي المتاح بعد خصم بدلات الإجازة السابقة هو {available:0.##} يوم.");
        }

        return (true, available, null);
    }
}
