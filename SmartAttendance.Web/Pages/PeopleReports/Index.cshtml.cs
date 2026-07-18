using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Reports;

namespace SmartAttendance.Web.Pages.PeopleReports;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ---- Run parameters ----
    [BindProperty(SupportsGet = true)]
    public int ReportId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ActiveOnly { get; set; } = true;

    // ---- Lists ----
    public List<PeopleReportsStore.SavedReport> SystemReports { get; set; } = new();
    public List<PeopleReportsStore.SavedReport> MyReports { get; set; } = new();
    public List<PeopleReportsStore.SavedReport> SharedReports { get; set; } = new();

    public IReadOnlyList<PeopleReportCatalog.ReportDataset> Datasets => PeopleReportCatalog.Datasets;

    public List<CompanyOption> Companies { get; set; } = new();

    // ---- Run result ----
    public PeopleReportsStore.SavedReport? Current { get; set; }
    public List<PeopleReportCatalog.ReportColumn> RunColumns { get; set; } = new();
    public List<Dictionary<string, string>> RunRows { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    private string CurrentUser => User.Identity?.Name ?? "System";

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadListsAsync();

        if (ReportId > 0)
        {
            Current = await PeopleReportsStore.GetAsync(_dbContext, ReportId);
            if (Current == null)
            {
                return RedirectToPage();
            }

            await RunAsync(Current);
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var report = await PeopleReportsStore.GetAsync(_dbContext, ReportId);
        if (report == null)
        {
            return RedirectToPage();
        }

        await RunAsync(report);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", RunColumns.Select(c => Csv(c.Label))));
        foreach (var row in RunRows)
        {
            sb.AppendLine(string.Join(",", RunColumns.Select(c => Csv(row.GetValueOrDefault(c.Key, "")))));
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();

        return File(bytes, "text/csv", $"report-{report.Id}.csv");
    }

    public async Task<IActionResult> OnPostCreateReportAsync(
        string name, string? description, string datasetKey, string columnsCsv, string visibility)
    {
        await PeopleReportsStore.EnsureSchemaAsync(_dbContext);

        var dataset = PeopleReportCatalog.GetDataset(datasetKey ?? "");
        name = (name ?? "").Trim();

        if (dataset == null || string.IsNullOrWhiteSpace(name))
        {
            Message = "اسم التقرير ومصدر البيانات مطلوبان.";
            return RedirectToPage();
        }

        // Ordered columns come from the dual-listbox as CSV; keep only valid keys.
        var validColumns = (columnsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => dataset.Columns.Any(dc => dc.Key.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        if (validColumns.Count == 0)
        {
            Message = "اختر عموداً واحداً على الأقل.";
            return RedirectToPage();
        }

        var isShared = string.Equals(visibility, "everyone", StringComparison.OrdinalIgnoreCase);

        await PeopleReportsStore.CreateAsync(
            _dbContext, name, description, dataset.Key, string.Join(",", validColumns), CurrentUser, isShared);

        Message = "تم حفظ التقرير.";
        return RedirectToPage(null, null, null, "mine");
    }

    public async Task<IActionResult> OnPostDeleteReportAsync(int id)
    {
        await PeopleReportsStore.DeleteOwnAsync(_dbContext, id, CurrentUser);
        Message = "تم حذف التقرير.";
        return RedirectToPage(null, null, null, "mine");
    }

    public async Task<IActionResult> OnPostToggleShareAsync(int id)
    {
        await PeopleReportsStore.ToggleShareOwnAsync(_dbContext, id, CurrentUser);
        Message = "تم تحديث المشاركة.";
        return RedirectToPage(null, null, null, "mine");
    }

    private async Task LoadListsAsync()
    {
        var all = await PeopleReportsStore.LoadAllAsync(_dbContext);

        SystemReports = all.Where(r => r.IsSystem).ToList();
        MyReports = all.Where(r => !r.IsSystem && string.Equals(r.OwnerUser, CurrentUser, StringComparison.OrdinalIgnoreCase)).ToList();
        SharedReports = all.Where(r => !r.IsSystem && r.IsShared && !string.Equals(r.OwnerUser, CurrentUser, StringComparison.OrdinalIgnoreCase)).ToList();

        Companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }

    private async Task RunAsync(PeopleReportsStore.SavedReport report)
    {
        var dataset = PeopleReportCatalog.GetDataset(report.DatasetKey);
        if (dataset == null)
        {
            return;
        }

        RunColumns = report.Columns
            .Select(key => dataset.Columns.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        if (RunColumns.Count == 0)
        {
            RunColumns = dataset.Columns.ToList();
        }

        RunRows = await PeopleReportCatalog.LoadAsync(
            _dbContext,
            report.DatasetKey,
            report.FilterKey,
            new PeopleReportCatalog.ReportFilters
            {
                CompanyId = CompanyId,
                Search = Search,
                ActiveOnly = ActiveOnly
            });
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
