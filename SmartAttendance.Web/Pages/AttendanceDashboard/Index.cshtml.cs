using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceDashboard;

/// <summary>
/// رسومات مودل الحضور (/AttendanceDashboard) — المجموعة الأولى بمودل كيان
/// («رسومات بيانية»)، وآخر ما كان ناقصاً بجرد الشاشات بعد «العمل خارج المكتب».
///
/// <para>قراءة محضة: لا تكتب شيئاً ولا تشغّل تحليلاً. مصدرها اليوميات المحلَّلة
/// عبر <see cref="DayAttendanceStore.ListRangeAsync"/> — نفس قراءة الحضور اليومي
/// وبنفس فترة الغلق، فلا يقع أن تعرض اللوحة رقماً والشاشة رقماً آخر لنفس الفترة.</para>
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext dbContext,
        SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    public AttendancePeriodPolicy.Period CutoffPeriod { get; private set; }

    public string? CutoffPolicyName { get; private set; }

    public int PresentDays { get; private set; }
    public int LateDays { get; private set; }
    public int IncompleteDays { get; private set; }
    public int AbsentDays { get; private set; }
    public int StaleDays { get; private set; }
    public int AnalyzedDays { get; private set; }
    public int Employees { get; private set; }

    public decimal LateHours { get; private set; }
    public decimal EarlyLeaveHours { get; private set; }
    public decimal WorkedHours { get; private set; }

    /// <summary>نسبة الالتزام = (حاضر + متأخر) ÷ اليوميات المحلَّلة.</summary>
    public int AttendanceRate => AnalyzedDays == 0
        ? 0
        : (int)Math.Round((PresentDays + LateDays) * 100.0 / AnalyzedDays);

    /// <summary>توزيع الحالات — للأعمدة الأفقية. مرتّب تنازلياً بالعدد.</summary>
    public List<StatusSlice> Distribution { get; private set; } = new();

    /// <summary>أكثر الموظفين تأخّراً بالفترة (بالساعات) — العشرة الأوائل.</summary>
    public List<EmployeeStat> TopLate { get; private set; } = new();

    /// <summary>أكثر الموظفين غياباً بالفترة (بالأيام) — العشرة الأوائل.</summary>
    public List<EmployeeStat> TopAbsent { get; private set; } = new();

    public (int Year, int Month) Period
    {
        get
        {
            if (DateTime.TryParse($"{Month}-01", out var parsed)) return (parsed.Year, parsed.Month);
            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }

    public async Task OnGetAsync()
    {
        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        (CutoffPeriod, CutoffPolicyName) =
            await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);

        var days = await DayAttendanceStore.ListRangeAsync(
            _dbContext, await _companyScope.GetAsync(), CutoffPeriod.From, CutoffPeriod.To, search: null);

        if (days.Count == 0) return;

        AnalyzedDays = days.Count;
        Employees = days.Select(day => day.EmployeeId).Distinct().Count();

        PresentDays = days.Count(day => day.Status == "Present");
        LateDays = days.Count(day => day.Status == "Late");
        IncompleteDays = days.Count(day => day.Status == "Incomplete");
        AbsentDays = days.Count(day => day.Status == "Absent");
        StaleDays = days.Count(day => day.IsStale);

        LateHours = Math.Round(days.Sum(day => day.LateHours), 2);
        EarlyLeaveHours = Math.Round(days.Sum(day => day.EarlyLeaveHours), 2);
        WorkedHours = Math.Round(days.Sum(day => day.WorkedHours), 2);

        Distribution = days
            .GroupBy(day => day.Status)
            .Select(group => new StatusSlice(
                group.Key,
                DayAttendanceStore.StatusLabel(group.Key),
                group.Count(),
                (int)Math.Round(group.Count() * 100.0 / AnalyzedDays)))
            .OrderByDescending(slice => slice.Count)
            .ToList();

        // «الأكثر تأخّراً» بالساعات لا بعدد الأيام: موظفٌ تأخّر يوماً ثلاث ساعات
        // أسوأ من آخرَ تأخّر ثلاثة أيام خمس دقائق — والعدّ وحده يقلب الترتيب.
        TopLate = days
            .Where(day => day.LateHours > 0)
            .GroupBy(day => (day.EmployeeNo, day.EmployeeName))
            .Select(group => new EmployeeStat(
                group.Key.EmployeeNo,
                group.Key.EmployeeName,
                Math.Round(group.Sum(day => day.LateHours), 2),
                group.Count()))
            .OrderByDescending(stat => stat.Value)
            .Take(10)
            .ToList();

        TopAbsent = days
            .Where(day => day.Status == "Absent")
            .GroupBy(day => (day.EmployeeNo, day.EmployeeName))
            .Select(group => new EmployeeStat(
                group.Key.EmployeeNo,
                group.Key.EmployeeName,
                group.Count(),
                group.Count()))
            .OrderByDescending(stat => stat.Value)
            .Take(10)
            .ToList();
    }

    public sealed record StatusSlice(string Key, string Label, int Count, int Percent);

    /// <summary><c>Value</c> ساعات بالتأخير وأيام بالغياب؛ <c>Days</c> عدد اليوميات.</summary>
    public sealed record EmployeeStat(string EmployeeNo, string EmployeeName, decimal Value, int Days);
}
