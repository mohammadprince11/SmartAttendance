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
/// عبر <see cref="DayAttendanceStore.DashboardAggregateAsync"/> — تجميعةٌ بالـSQL
/// على نفس جدول الحضور اليومي وبنفس فترة الغلق، فلا يقع أن تعرض اللوحة رقماً
/// والشاشة رقماً آخر لنفس الفترة.</para>
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

        // التجميعة كاملةً بالـSQL: لا تُشحن يوميات المدى (~80 ألف صفّ) إلى التطبيق
        // لتُعدّ بالذاكرة. اللوحة تقرأ IsStale فلا يمكنها تخطّي حسابه كبقية الشاشات،
        // فيُحسب مجموعياً بالـSQL بدل EXISTS مترابط لكل صفّ.
        var agg = await DayAttendanceStore.DashboardAggregateAsync(
            _dbContext, await _companyScope.GetAsync(), CutoffPeriod.From, CutoffPeriod.To);

        if (agg.AnalyzedDays == 0) return;

        AnalyzedDays = agg.AnalyzedDays;
        Employees = agg.Employees;

        PresentDays = agg.PresentDays;
        LateDays = agg.LateDays;
        IncompleteDays = agg.IncompleteDays;
        AbsentDays = agg.AbsentDays;
        StaleDays = agg.StaleDays;

        LateHours = agg.LateHours;
        EarlyLeaveHours = agg.EarlyLeaveHours;
        WorkedHours = agg.WorkedHours;

        Distribution = agg.StatusCounts
            .Select(sc => new StatusSlice(
                sc.Status,
                DayAttendanceStore.StatusLabel(sc.Status),
                sc.Count,
                (int)Math.Round(sc.Count * 100.0 / AnalyzedDays)))
            .OrderByDescending(slice => slice.Count)
            .ToList();

        // «الأكثر تأخّراً» بالساعات لا بعدد الأيام: موظفٌ تأخّر يوماً ثلاث ساعات
        // أسوأ من آخرَ تأخّر ثلاثة أيام خمس دقائق — والعدّ وحده يقلب الترتيب.
        TopLate = agg.TopLate
            .Select(s => new EmployeeStat(s.EmployeeNo, s.EmployeeName, s.Value, s.Days))
            .ToList();

        TopAbsent = agg.TopAbsent
            .Select(s => new EmployeeStat(s.EmployeeNo, s.EmployeeName, s.Value, s.Days))
            .ToList();
    }

    public sealed record StatusSlice(string Key, string Label, int Count, int Percent);

    /// <summary><c>Value</c> ساعات بالتأخير وأيام بالغياب؛ <c>Days</c> عدد اليوميات.</summary>
    public sealed record EmployeeStat(string EmployeeNo, string EmployeeName, decimal Value, int Days);
}
