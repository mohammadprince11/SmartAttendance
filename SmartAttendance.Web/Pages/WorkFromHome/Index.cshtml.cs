using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.WorkFromHome;

/// <summary>
/// العمل خارج المكتب (/WorkFromHome) — «العمل من المنزل» بقائمة إدارة الحضور عند
/// كيان، وهو البند الوحيد الذي كان ناقصاً بجرد الشاشات (2026-08-07).
///
/// <para>⚠️ كيان يعرض البند بلا صفحة على مستأجر الديمو (<c>href="#"</c>) — فلا مواصفة
/// نُقلّدها. البناء هنا **قرار منتج** اتُّخذ صراحةً: العمل خارج المكتب **سياق يوم**
/// يُنتَج من طلبٍ معتمد بالخدمة الذاتية، بأثرٍ ماليّ **محايد** يتركه لمنشئ القواعد.</para>
///
/// <para>الشاشة قراءة فقط عمداً: الإنشاء بالخدمة الذاتية (<c>/SelfServices</c>)
/// والاعتماد بمساره المركزي (<c>/Approvals</c>). تكرار الإنشاء هنا كان يعني مسار
/// اعتمادٍ ثانياً يلتفّ على الأول — وهو ما يجعل الطلب معتمداً بشاشة وغير معتمد
/// بأخرى.</para>
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>فارغ = النوعان. وإلا مفتاح من <see cref="DayAttendanceStore.RequestTypeDayKinds"/>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    public List<DayRow> Rows { get; set; } = new();

    public int RemoteDays { get; set; }

    public int BusinessTripDays { get; set; }

    /// <summary>
    /// أيام معتمَدة للعمل خارج المكتب ومع ذلك حالتها «غياب». ليست خطأً بالنظام بل
    /// **النتيجة المباشرة للأثر المحايد**: بلا بصمة يبقى اليوم غياباً حتى لو كان
    /// العمل عن بُعد معتمَداً. تُعرض صراحةً لأنها المفاجأة الوحيدة بهذا التصميم.
    /// </summary>
    public int AbsentDespiteApproval { get; set; }

    /// <summary>الفترة الفعلية بعد سياسة الغلق — نفس فترة الحضور اليومي.</summary>
    public AttendancePeriodPolicy.Period CutoffPeriod { get; private set; }

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

        (CutoffPeriod, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);

        // المصدر هو اليوميات المحلَّلة لا الطلبات: الطلب نيّة، واليومية هي ما دخل
        // المحرّك فعلاً. عرض الطلبات هنا كان يُظهر أياماً «معتمدة» لم تُحلَّل بعد
        // فتبدو مطبَّقة وهي ليست كذلك.
        var days = await DayAttendanceStore.ListRangeAsync(
            _dbContext, CutoffPeriod.From, CutoffPeriod.To, Search);

        var outOfOffice = days
            .Where(day => day.DayKind is DayAttendanceStore.RemoteDayKind
                                      or DayAttendanceStore.BusinessTripDayKind)
            .ToList();

        RemoteDays = outOfOffice.Count(day => day.DayKind == DayAttendanceStore.RemoteDayKind);
        BusinessTripDays = outOfOffice.Count(day => day.DayKind == DayAttendanceStore.BusinessTripDayKind);
        AbsentDespiteApproval = outOfOffice.Count(day => day.Status == "Absent");

        if (!string.IsNullOrWhiteSpace(Kind)
            && DayAttendanceStore.RequestTypeDayKinds.TryGetValue(Kind, out var dayKind))
        {
            outOfOffice = outOfOffice.Where(day => day.DayKind == dayKind).ToList();
        }

        Rows = outOfOffice
            .OrderByDescending(day => day.WorkDate)
            .ThenBy(day => day.EmployeeNo, StringComparer.Ordinal)
            .Select(day => new DayRow
            {
                EmployeeNo = day.EmployeeNo,
                EmployeeName = day.EmployeeName,
                WorkDate = day.WorkDate,
                DayKind = day.DayKind,
                CheckIn = day.CheckIn,
                CheckOut = day.CheckOut,
                WorkedHours = day.WorkedHours,
                LateHours = day.LateHours,
                Status = day.Status
            })
            .ToList();
    }

    public sealed class DayRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public string DayKind { get; set; } = string.Empty;
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public decimal WorkedHours { get; set; }
        public decimal LateHours { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
