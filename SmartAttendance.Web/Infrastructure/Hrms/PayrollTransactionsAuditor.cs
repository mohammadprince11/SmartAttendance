using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مدقق حركات الرواتب — نظير «مدقق حركات كيان»: كاشف شذوذ يعرض لكل موظف عدد الحالات
/// المشبوهة بحركات فترة، مع تفصيلها. **قراءة فقط**؛ لا يعدّل ولا يحذف — القرار للمدقق.
///
/// القواعد بيانات لا تخمين، وكلها قابلة للشرح بجملة:
///  • <b>مكرر</b>: نفس الموظف+العنصر+المبلغ+الفترة أكثر من مرة (خطأ إدخال أو استيراد مزدوج).
///  • <b>مبلغ صفري</b>: حركة بمبلغ 0 وبلا ساعات/أيام — لا أثر ولا معنى.
///  • <b>سالب</b>: دخل بمبلغ سالب أو اقتطاع سالب — إشارة معكوسة.
///  • <b>ساعات إضافي مفرطة</b>: أكثر من <see cref="MaxOvertimeHoursPerTx"/> ساعة بحركة واحدة.
///  • <b>اقتطاع يفوق الأساسي</b>: حركة اقتطاع واحدة تتجاوز الراتب الأساسي.
///  • <b>مسودّة قديمة</b>: مسودّة بفترةٍ سبق أن قُفلت دفعتها — لن تدخل مسيراً أبداً.
/// </summary>
public static class PayrollTransactionsAuditor
{
    public const decimal MaxOvertimeHoursPerTx = 100m;

    public sealed record Finding(
        int EmployeeId, string EmployeeNo, string EmployeeName,
        string Rule, string Detail, string? ReferenceNo, decimal Amount);

    public sealed record EmployeeSummary(int EmployeeId, string EmployeeNo, string EmployeeName, int Cases);

    private sealed record Row(
        int Id, int EmployeeId, string EmployeeNo, string EmployeeName, decimal Basic,
        string TxType, string ItemName, decimal Amount, decimal? Hours, decimal? Days,
        int Year, int Month, string Status, string ReferenceNo, bool PeriodLocked);

    /// <summary>القواعد النقية — تعمل على صفوف بالذاكرة (قابلة للاختبار بلا قاعدة).</summary>
    public static List<Finding> Analyze(IReadOnlyList<(int Id, int EmployeeId, string EmployeeNo, string EmployeeName, decimal Basic,
        string TxType, string ItemName, decimal Amount, decimal? Hours, decimal? Days, int Year, int Month, string Status, string ReferenceNo, bool PeriodLocked)> rows)
    {
        var findings = new List<Finding>();

        // مكرر
        foreach (var g in rows.GroupBy(r => (r.EmployeeId, r.TxType, r.ItemName, r.Amount, r.Year, r.Month)).Where(g => g.Count() > 1))
        {
            var f = g.First();
            findings.Add(new Finding(f.EmployeeId, f.EmployeeNo, f.EmployeeName, "مكرر",
                $"{g.Count()} حركات متطابقة ({f.ItemName} {f.Amount:#,0.##}) بفترة {f.Month:00}/{f.Year}", f.ReferenceNo, f.Amount));
        }

        foreach (var r in rows)
        {
            if (r.Amount == 0 && (r.Hours ?? 0) == 0 && (r.Days ?? 0) == 0)
                findings.Add(new Finding(r.EmployeeId, r.EmployeeNo, r.EmployeeName, "مبلغ صفري",
                    $"{r.ItemName} بلا مبلغ ولا ساعات/أيام", r.ReferenceNo, r.Amount));

            if (r.Amount < 0 && r.TxType is "Income" or "Deduction")
                findings.Add(new Finding(r.EmployeeId, r.EmployeeNo, r.EmployeeName, "سالب",
                    $"{PayrollTransactionStore.TypeLabel(r.TxType)} بمبلغ سالب {r.Amount:#,0.##}", r.ReferenceNo, r.Amount));

            if (r.TxType == "Overtime" && (r.Hours ?? 0) > MaxOvertimeHoursPerTx)
                findings.Add(new Finding(r.EmployeeId, r.EmployeeNo, r.EmployeeName, "ساعات إضافي مفرطة",
                    $"{r.Hours:0.##} ساعة بحركة واحدة (الحد {MaxOvertimeHoursPerTx})", r.ReferenceNo, r.Amount));

            if (r.TxType == "Deduction" && r.Basic > 0 && r.Amount > r.Basic)
                findings.Add(new Finding(r.EmployeeId, r.EmployeeNo, r.EmployeeName, "اقتطاع يفوق الأساسي",
                    $"{r.Amount:#,0.##} > الأساسي {r.Basic:#,0.##}", r.ReferenceNo, r.Amount));

            if (r.Status == "Draft" && r.PeriodLocked)
                findings.Add(new Finding(r.EmployeeId, r.EmployeeNo, r.EmployeeName, "مسودّة بفترة مقفلة",
                    $"{r.ItemName} مسودّة بفترة {r.Month:00}/{r.Year} سبق قفل دفعتها — لن تدخل مسيراً", r.ReferenceNo, r.Amount));
        }

        return findings;
    }

    /// <summary>يحمّل حركات الفترة (أو كلها إن لم تُحدَّد) ضمن نطاق الشركات ويحلّلها.</summary>
    public static async Task<List<Finding>> AuditAsync(ApplicationDbContext db, CompanyScope scope, int? year, int? month)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new();
        await PayrollTransactionStore.EnsureAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT t.Id, t.EmployeeId, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS EmployeeName,
       ISNULL(e.BasicSalary, 0) AS Basic, t.TxType, ISNULL(t.ItemName, N'') AS ItemName, t.Amount, t.Hours, t.Days,
       t.[Year], t.[Month], ISNULL(t.Status, N'Approved') AS Status, ISNULL(t.ReferenceNo, N'') AS ReferenceNo,
       CASE WHEN EXISTS (SELECT 1 FROM PayrollRuns r WHERE r.[Year] = t.[Year] AND r.[Month] = t.[Month]
                          AND r.Status IN (N'Locked', N'Issued', N'PayslipSent')) THEN 1 ELSE 0 END AS PeriodLocked
FROM PayrollTransactions t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE ISNULL(e.IsDeleted, 0) = 0
  AND (@Y IS NULL OR t.[Year] = @Y) AND (@M IS NULL OR t.[Month] = @M)
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", (object?)year ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@M", (object?)month ?? DBNull.Value);
            },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                reader["Basic"] is decimal b ? b : 0m,
                HrmsDatabase.GetString(reader, "TxType"),
                HrmsDatabase.GetString(reader, "ItemName"),
                reader["Amount"] is decimal a ? a : 0m,
                reader["Hours"] is decimal h ? h : (decimal?)null,
                reader["Days"] is decimal d ? d : (decimal?)null,
                HrmsDatabase.GetInt(reader, "Year"),
                HrmsDatabase.GetInt(reader, "Month"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetString(reader, "ReferenceNo"),
                HrmsDatabase.GetBool(reader, "PeriodLocked")));

        return Analyze(rows);
    }

    public static List<EmployeeSummary> Summarize(IEnumerable<Finding> findings) =>
        findings.GroupBy(f => (f.EmployeeId, f.EmployeeNo, f.EmployeeName))
                .Select(g => new EmployeeSummary(g.Key.EmployeeId, g.Key.EmployeeNo, g.Key.EmployeeName, g.Count()))
                .OrderByDescending(s => s.Cases).ThenBy(s => s.EmployeeNo)
                .ToList();
}
