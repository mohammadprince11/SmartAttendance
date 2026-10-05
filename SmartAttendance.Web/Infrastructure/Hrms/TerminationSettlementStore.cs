using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة ما اقتُطع فعلاً من موظف خلال سنة، لحساب بنود «الفروقات» بتسوية الإنهاء.
///
/// المصدر المالي هو <c>PayrollRunLines</c> من المسيرات النظامية النهائية غير
/// المعكوسة؛ بذلك لا نعتمد أسماء البنود ولا نحسب Draft/Reversal كاقتطاع فعلي.
///
/// ⚠️ <b>قراءة فقط.</b> هذا الملف لا يكتب بالمسير ولا يغيّر قسيمة صادرة.
/// </summary>
public static class TerminationSettlementStore
{
    public sealed record YearWithholding(decimal Tax, decimal Gosi, int MonthsPaid, int? LastMonthKey);

    /// <summary>
    /// إجمالي Tax/GOSI المقتطع فعلياً من المسيرات النظامية النهائية غير المعكوسة.
    /// Draft/Calculated وOffCycle/Retroactive لا تُعدّ راتباً نظامياً مدفوعاً هنا،
    /// والمسير الأصلي يُستبعد للموظف إذا وُجد له Reversal نهائي.
    /// </summary>
    public static async Task<YearWithholding> LoadYearAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        int employeeId,
        int year)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new YearWithholding(0m, 0m, 0, null);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT r.[Year], r.[Month],
       SUM(ISNULL(l.TaxAmount,0)) AS TaxAmount,
       SUM(ISNULL(l.GosiEmployee,0)) AS GosiEmployee
FROM PayrollRunLines l
INNER JOIN PayrollRuns r ON r.Id = l.RunId
INNER JOIN Employees e ON e.Id = l.EmployeeId
WHERE l.EmployeeId = @EmployeeId
  AND r.[Year] = @Year
  AND ISNULL(r.RunType,N'Regular') = N'Regular'
  AND r.Status IN (N'Locked',N'Issued',N'PayslipSent')
  AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
  AND NOT EXISTS (
      SELECT 1
      FROM PayrollRuns rr
      INNER JOIN PayrollRunLines rl
          ON rl.RunId = rr.Id AND rl.EmployeeId = l.EmployeeId
      WHERE rr.OriginalRunId = r.Id
        AND rr.RunType = N'Reversal'
        AND rr.Status IN (N'Locked',N'Issued',N'PayslipSent')
  )
GROUP BY r.[Year], r.[Month];
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Year", year);
            },
            reader => (
                Month: HrmsDatabase.GetInt(reader, "Month"),
                Tax: HrmsDatabase.GetNullableDecimal(reader, "TaxAmount") ?? 0m,
                Gosi: HrmsDatabase.GetNullableDecimal(reader, "GosiEmployee") ?? 0m));

        var months = rows.Select(row => row.Month).Where(month => month > 0).Distinct().ToList();

        return new YearWithholding(
            Math.Round(rows.Sum(row => row.Tax), 2, MidpointRounding.AwayFromZero),
            Math.Round(rows.Sum(row => row.Gosi), 2, MidpointRounding.AwayFromZero),
            months.Count,
            months.Count == 0 ? null : TerminationSettlementPolicy.MonthKey(year, months.Max()));
    }

    /// <summary>
    /// آخر شهر مسير نظامي نهائي وغير معكوس للموظف — OffCycle لا يغطي راتب شهر الإنهاء.
    /// </summary>
    public static async Task<int?> LastPaidMonthKeyAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        int employeeId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return null;

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT TOP 1 r.[Year], r.[Month]
FROM PayrollRunLines l
INNER JOIN PayrollRuns r ON r.Id = l.RunId
INNER JOIN Employees e ON e.Id = l.EmployeeId
WHERE l.EmployeeId = @EmployeeId
  AND ISNULL(r.RunType,N'Regular') = N'Regular'
  AND r.Status IN (N'Locked',N'Issued',N'PayslipSent')
  AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
  AND NOT EXISTS (
      SELECT 1
      FROM PayrollRuns rr
      INNER JOIN PayrollRunLines rl
          ON rl.RunId = rr.Id AND rl.EmployeeId = l.EmployeeId
      WHERE rr.OriginalRunId = r.Id
        AND rr.RunType = N'Reversal'
        AND rr.Status IN (N'Locked',N'Issued',N'PayslipSent')
  )
ORDER BY r.[Year] DESC, r.[Month] DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => TerminationSettlementPolicy.MonthKey(
                HrmsDatabase.GetInt(reader, "Year"),
                HrmsDatabase.GetInt(reader, "Month")));

        return rows.FirstOrDefault() is var key && key != 0 ? key : null;
    }
}
