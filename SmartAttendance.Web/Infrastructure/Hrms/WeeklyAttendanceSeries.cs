using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// «حضور الأسبوع» بلوحة البيانات: نسبة الحضور لكل يوم من الأيام السبعة الأخيرة.
///
/// قاعدة الصدق هنا: يوم بلا أيام محلَّلة (<c>IsAnalyzed = 0</c> أو لا صفوف) يُعرض
/// **«—» لا 0%**. صفر بالمئة يعني «لم يحضر أحد»، وهو ادّعاء مختلف تماماً عن
/// «لم يُحلَّل بعد» — والخلط بينهما يجعل المدير يطارد مشكلة غير موجودة.
/// </summary>
public static class WeeklyAttendanceSeries
{
    public sealed record DayBar(DateOnly Date, string Label, int? Percent, bool IsToday)
    {
        /// <summary>ارتفاع العمود (٪ من ارتفاع الرسم) — لليوم بلا بيانات جذعٌ باهت.</summary>
        public int BarHeight => Percent is { } value ? Math.Clamp(value, 4, 100) : 6;

        public string Display => Percent is { } value ? $"{value}%" : "—";

        public bool HasData => Percent.HasValue;
    }

    /// <summary>عدّادات يوم واحد كما تُقرأ من الجدول.</summary>
    public readonly record struct DayCounts(DateOnly Date, int Present, int Analyzed);

    private static readonly string[] ArabicDayNames =
        ["الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    /// <summary>
    /// يبني الأعمدة السبعة المنتهية باليوم الحالي. نقي بالكامل: يأخذ العدّادات
    /// ويرجع ما يُرسم — فتُختبر قواعد النسبة و«—» واليوم الحالي بلا قاعدة بيانات.
    /// </summary>
    public static IReadOnlyList<DayBar> Build(IEnumerable<DayCounts> counts, DateOnly today)
    {
        var byDate = counts.ToDictionary(c => c.Date);
        var bars = new List<DayBar>(7);

        for (var offset = 6; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            int? percent = null;

            if (byDate.TryGetValue(date, out var day) && day.Analyzed > 0)
            {
                percent = (int)Math.Round(day.Present * 100.0 / day.Analyzed);
            }

            bars.Add(new DayBar(
                date,
                offset == 0 ? "اليوم" : ArabicDayNames[(int)date.DayOfWeek],
                percent,
                offset == 0));
        }

        return bars;
    }

    public static async Task<IReadOnlyList<DayBar>> LoadAsync(
        ApplicationDbContext dbContext, int companyId, DateOnly today)
    {
        await DayAttendanceStore.EnsureAsync(dbContext);

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT
    CAST(d.WorkDate AS date) AS WorkDate,
    SUM(CASE WHEN d.Status IN ('Present', 'Late') THEN 1 ELSE 0 END) AS Present,
    COUNT(*) AS Analyzed
FROM DayAttendances d
INNER JOIN Employees e ON d.EmployeeId = e.Id
INNER JOIN Branches  b ON e.BranchId = b.Id
WHERE b.CompanyId = @Company
  AND d.IsAnalyzed = 1
  AND d.WorkDate >= @From
  AND d.WorkDate <= @To
GROUP BY CAST(d.WorkDate AS date);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Company", companyId);
                HrmsDatabase.AddParameter(command, "@From", today.AddDays(-6).ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", today.ToDateTime(TimeOnly.MinValue));
            },
            reader => new DayCounts(
                DateOnly.FromDateTime(HrmsDatabase.GetDateTime(reader, "WorkDate") ?? DateTime.Today),
                HrmsDatabase.GetInt(reader, "Present"),
                HrmsDatabase.GetInt(reader, "Analyzed")));

        return Build(rows, today);
    }
}
