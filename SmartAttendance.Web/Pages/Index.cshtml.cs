using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages;

/// <summary>
/// لوحة التحكم القابلة للبناء (نمط لوحة أشخاص كيان): صف عدادات + شبكة بطاقات
/// رسوم 3 أعمدة، وكل شيء ويدجتات من DashboardWidgetStore — المستخدم يضيف/يخفي/
/// يرتب ويختار شكل العرض من درج «تخصيص اللوحة».
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext dbContext,
        ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName =>
        CompanyId.HasValue
            ? CompanyOptions.FirstOrDefault(x => x.Id == CompanyId.Value)?.Name ?? "-"
            : "-";

    /// <summary>كل الويدجتات (للدرج) — والمرئية مع بياناتها للعرض.</summary>
    public List<DashboardWidgetStore.Widget> AllWidgets { get; set; } = new();
    public List<(DashboardWidgetStore.Widget Widget, DashboardWidgetStore.WidgetData Data)> VisibleWidgets { get; set; } = new();

    // ===== طبقة «مهمة أولاً» (Design System v1) =====
    // تُضاف فوق شبكة الويدجتات ولا تلغيها: الويدجتات ميزة قائمة يبنيها المستخدم
    // بنفسه، وحذفها لإحلال تصميم جديد يهدم عملاً موجوداً.

    public int ActiveEmployees { get; private set; }
    public int PresentToday { get; private set; }
    public int AbsentToday { get; private set; }
    public int PendingRequests { get; private set; }
    public int ExpiringContracts { get; private set; }

    public IReadOnlyList<DashboardTaskBuilder.DashboardTask> Tasks { get; private set; } =
        Array.Empty<DashboardTaskBuilder.DashboardTask>();

    public string Headline { get; private set; } = string.Empty;

    /// <summary>أعمدة حضور الأيام السبعة الأخيرة (يوم بلا تحليل يظهر «—» لا 0%).</summary>
    public IReadOnlyList<WeeklyAttendanceSeries.DayBar> WeekBars { get; private set; } =
        Array.Empty<WeeklyAttendanceSeries.DayBar>();

    /// <summary>نسبة الحضور من النشطين — للعرض بجانب العدّاد.</summary>
    public int PresentPercent =>
        ActiveEmployees > 0
            ? (int)Math.Round(PresentToday * 100.0 / ActiveEmployees)
            : 0;

    public string Greeting => DateTime.Now.Hour switch
    {
        >= 5 and < 12 => "صباح الخير",
        >= 12 and < 17 => "طاب يومك",
        _ => "مساء الخير"
    };

    public async Task OnGetAsync()
    {
        // اللوحة يصلها كل دور؛ فالمُنتقي محصورٌ بشركات المستخدم كي لا يرى دورٌ مقيَّد
        // أسماء/أعداد شركةٍ أخرى، ولا يُنفَّذ أي مقياس لشركةٍ خارج نطاقه. و`Resolve`
        // يرفض أيضاً CompanyId مُمرَّراً بالرابط خارج النطاق (الويدجتات تعريفاتٌ عامّة
        // لا بياناتُ مستأجر، فتبقى كما هي).
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        CompanyOptions = (await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => new CompanyOption
            {
                Id = x.Id,
                Name = x.Name,
                EmployeeCount = _dbContext.Employees.Count(e =>
                    !e.IsDeleted && e.Branch != null && e.Branch.CompanyId == x.Id)
            })
            .OrderByDescending(x => x.EmployeeCount)
            .ThenBy(x => x.Name)
            .ToListAsync())
            .Where(x => scope.Allows(x.Id))
            .ToList();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        // حارس دفاعيّ صريح: لا يُنفَّذ أي مقياس إلا لشركةٍ يسمح بها النطاق.
        if (!CompanyId.HasValue || !scope.Allows(CompanyId.Value))
        {
            CompanyId = null;
            return;
        }

        AllWidgets = await DashboardWidgetStore.ListAsync(_dbContext, scope, CompanyId.Value);

        await LoadTaskFirstLayerAsync(CompanyId.Value);

        foreach (var widget in AllWidgets.Where(w => w.IsVisible))
        {
            var data = await DashboardWidgetStore.ExecuteAsync(_dbContext, widget.Metric, CompanyId.Value);
            VisibleWidgets.Add((widget, data));
        }
    }

    /// <summary>
    /// مؤشرات الرأس والمهام — كلها من مصادر حقيقية مقيَّدة بالشركة المختارة:
    /// المقاييس تعيد استخدام <see cref="DashboardWidgetStore.ExecuteAsync"/> (نفس
    /// الاستعلامات المُختبرة) بدل كتابة SQL موازٍ يتفرّع عنها لاحقاً.
    /// </summary>
    private async Task LoadTaskFirstLayerAsync(int companyId)
    {
        async Task<int> CounterAsync(string metric) =>
            (await DashboardWidgetStore.ExecuteAsync(_dbContext, metric, companyId)).Single;

        ActiveEmployees = await CounterAsync("ActiveEmployees");
        PresentToday = await CounterAsync("TodayPresent");
        AbsentToday = await CounterAsync("TodayAbsent");
        PendingRequests = await CounterAsync("PendingRequests");
        ExpiringContracts = await CounterAsync("ExpiringContracts60");

        Tasks = DashboardTaskBuilder.Build(new DashboardTaskBuilder.Counts(
            PendingRequests: PendingRequests,
            PendingBiometricKeys: await CountPendingBiometricKeysAsync(companyId),
            ExpiringContracts: ExpiringContracts,
            // الغياب «بلا مبرر» يحتاج تحليل الأعذار؛ حتى يُبنى مقياسه لا نعرض
            // رقماً مضلّلاً — صفر هنا يعني «لا نقيسه بعد» لا «لا يوجد غياب».
            UnexcusedAbsenceToday: 0));

        Headline = DashboardTaskBuilder.BuildHeadline(Tasks);

        try
        {
            WeekBars = await WeeklyAttendanceSeries.LoadAsync(
                _dbContext, companyId, DateOnly.FromDateTime(DateTime.Today));
        }
        catch
        {
            // رسم ثانوي: تعذّر قراءته لا يُسقط اللوحة ولا يُظهر أرقاماً مخترَعة.
            WeekBars = Array.Empty<WeeklyAttendanceSeries.DayBar>();
        }
    }

    /// <summary>مفاتيح البصمة المعلّقة — مقيَّدة بموظفي الشركة المختارة.</summary>
    private async Task<int> CountPendingBiometricKeysAsync(int companyId)
    {
        try
        {
            return await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                """
IF OBJECT_ID('EmployeeWebAuthnCredentials', 'U') IS NULL
    SELECT 0;
ELSE
    SELECT COUNT(*)
    FROM EmployeeWebAuthnCredentials c
    INNER JOIN Employees e ON e.Id = c.EmployeeId
    INNER JOIN Branches  b ON b.Id = e.BranchId
    WHERE c.Status = 'Pending' AND e.IsDeleted = 0 AND b.CompanyId = @CompanyId;
""",
                command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId));
        }
        catch
        {
            // تعذّر قراءة عدّاد ثانوي لا يُسقط اللوحة كلها.
            return 0;
        }
    }

    public async Task<IActionResult> OnPostAddWidgetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        var form = Request.Form;
        var metric = form["Metric"].ToString();

        if (DashboardWidgetStore.Metrics.Any(m => m.Key == metric))
        {
            var isCounter = DashboardWidgetStore.IsCounterMetric(metric);
            var kind = form["ChartKind"].ToString() is { Length: > 0 } chartKind ? chartKind : "Number";
            await DashboardWidgetStore.AddAsync(_dbContext, scope, new DashboardWidgetStore.Widget
            {
                CompanyId = CompanyId.Value,
                Title = form["Title"].ToString().Trim(),
                Metric = metric,
                ChartKind = isCounter ? "Number" : kind == "Number" ? "HBars" : kind
            });
            TempData["SuccessMessage"] = "أُضيف الويدجت.";
        }
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteWidgetAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        await DashboardWidgetStore.DeleteAsync(_dbContext, scope, CompanyId.Value, id);
        TempData["SuccessMessage"] = "حُذف الويدجت.";
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostSaveLayoutAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        var orderedIds = Request.Form["OrderedIds"]
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        var visibleIds = Request.Form["VisibleIds"]
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();

        await DashboardWidgetStore.SaveLayoutAsync(_dbContext, scope, CompanyId.Value, orderedIds, visibleIds);
        TempData["SuccessMessage"] = "حُفظ تخطيط اللوحة.";
        return RedirectToPage(new { CompanyId });
    }

    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }
}
