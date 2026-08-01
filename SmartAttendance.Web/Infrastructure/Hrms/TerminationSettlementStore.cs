using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة ما اقتُطع فعلاً من موظف خلال سنة، لحساب بنود «الفروقات» بتسوية الإنهاء.
///
/// المصدر <c>PayrollRunLineComponents</c> — الاقتطاعات مخزَّنة أصلاً بكل قسيمة،
/// فالبيانات موجودة والحساب وحده كان ناقصاً (كما خلصت الدراسة).
///
/// ⚠️ <b>قراءة فقط.</b> هذا الملف لا يكتب بالمسير ولا يغيّر قسيمة صادرة.
/// </summary>
public static class TerminationSettlementStore
{
    /// <summary>
    /// أنواع المكوّنات التي تُعدّ اقتطاعاً نظامياً. المطابقة بالاسم أو النوع لأن
    /// المسير يخزّن <c>Kind</c> لبعضها واسماً حرّاً لبعضها الآخر.
    /// </summary>
    private static readonly string[] TaxKeys = { "tax", "ضريبة" };
    private static readonly string[] GosiKeys = { "gosi", "socialsecurity", "ضمان" };

    public sealed record YearWithholding(decimal Tax, decimal Gosi, int MonthsPaid, int? LastMonthKey);

    /// <summary>
    /// إجمالي ما اقتُطع من الموظف بسنةٍ ما، وعدد الأشهر المسيَّرة، وآخر شهر —
    /// وهذا الأخير يغذّي تنبيه «لم يُحتسب راتب شهر الإنهاء».
    /// </summary>
    public static async Task<YearWithholding> LoadYearAsync(ApplicationDbContext db, int employeeId, int year)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT r.Year, r.Month, ISNULL(c.Kind, N'') AS Kind, ISNULL(c.ItemName, N'') AS ItemName,
       ISNULL(c.Amount, 0) AS Amount, ISNULL(c.IsAddition, 1) AS IsAddition
FROM PayrollRunLines l
INNER JOIN PayrollRuns r ON r.Id = l.RunId
LEFT JOIN PayrollRunLineComponents c ON c.LineId = l.Id
WHERE l.EmployeeId = @EmployeeId AND r.Year = @Year;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Year", year);
            },
            reader => (
                Month: HrmsDatabase.GetInt(reader, "Month"),
                Kind: HrmsDatabase.GetString(reader, "Kind"),
                Name: HrmsDatabase.GetString(reader, "ItemName"),
                Amount: HrmsDatabase.GetNullableDecimal(reader, "Amount") ?? 0,
                IsAddition: HrmsDatabase.GetBool(reader, "IsAddition")));

        decimal tax = 0, gosi = 0;

        foreach (var row in rows)
        {
            // الإضافات ليست اقتطاعاً — بندٌ باسم «ضريبة» لكنه إضافة يعني تسويةً
            // سابقة لا اقتطاعاً، وجمعه هنا كان سيضاعف الفرق.
            if (row.IsAddition) continue;

            var haystack = $"{row.Kind} {row.Name}".ToLowerInvariant();

            if (TaxKeys.Any(key => haystack.Contains(key))) tax += row.Amount;
            else if (GosiKeys.Any(key => haystack.Contains(key))) gosi += row.Amount;
        }

        var months = rows.Select(row => row.Month).Where(month => month > 0).Distinct().ToList();

        return new YearWithholding(
            Math.Round(tax, 2, MidpointRounding.AwayFromZero),
            Math.Round(gosi, 2, MidpointRounding.AwayFromZero),
            months.Count,
            months.Count == 0 ? null : TerminationSettlementPolicy.MonthKey(year, months.Max()));
    }

    /// <summary>آخر شهر مسيَّر للموظف عبر السنوات كلها — لتنبيه شهر الإنهاء.</summary>
    public static async Task<int?> LastPaidMonthKeyAsync(ApplicationDbContext db, int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1 r.Year, r.Month
FROM PayrollRunLines l
INNER JOIN PayrollRuns r ON r.Id = l.RunId
WHERE l.EmployeeId = @EmployeeId
ORDER BY r.Year DESC, r.Month DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => TerminationSettlementPolicy.MonthKey(
                HrmsDatabase.GetInt(reader, "Year"), HrmsDatabase.GetInt(reader, "Month")));

        return rows.FirstOrDefault() is var key && key != 0 ? key : null;
    }
}
