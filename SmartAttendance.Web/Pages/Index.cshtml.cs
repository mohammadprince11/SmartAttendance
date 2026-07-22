using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages;

/// <summary>
/// لوحة التحكم القابلة للبناء (نمط لوحة أشخاص كيان): صف عدادات + شبكة بطاقات
/// رسوم 3 أعمدة، وكل شيء ويدجتات من DashboardWidgetStore — المستخدم يضيف/يخفي/
/// يرتب ويختار شكل العرض من درج «تخصيص اللوحة».
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

    public async Task OnGetAsync()
    {
        CompanyOptions = await _dbContext.Companies
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
            .ToListAsync();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        AllWidgets = await DashboardWidgetStore.ListAsync(_dbContext);

        if (!CompanyId.HasValue)
        {
            return;
        }

        foreach (var widget in AllWidgets.Where(w => w.IsVisible))
        {
            var data = await DashboardWidgetStore.ExecuteAsync(_dbContext, widget.Metric, CompanyId.Value);
            VisibleWidgets.Add((widget, data));
        }
    }

    public async Task<IActionResult> OnPostAddWidgetAsync()
    {
        var form = Request.Form;
        var metric = form["Metric"].ToString();

        if (DashboardWidgetStore.Metrics.Any(m => m.Key == metric))
        {
            var isCounter = DashboardWidgetStore.IsCounterMetric(metric);
            var kind = form["ChartKind"].ToString() is { Length: > 0 } chartKind ? chartKind : "Number";
            await DashboardWidgetStore.AddAsync(_dbContext, new DashboardWidgetStore.Widget
            {
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
        await DashboardWidgetStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "حُذف الويدجت.";
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostSaveLayoutAsync()
    {
        var orderedIds = Request.Form["OrderedIds"]
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        var visibleIds = Request.Form["VisibleIds"]
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();

        await DashboardWidgetStore.SaveLayoutAsync(_dbContext, orderedIds, visibleIds);
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
