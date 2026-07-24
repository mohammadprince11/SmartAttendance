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

    // مدى تاريخ مصادر الحضور (بارامتر تشغيل نمط كيان). فارغ ⟶ الشهر الحالي.
    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    // ---- Lists ----
    public List<PeopleReportsStore.SavedReport> SystemReports { get; set; } = new();
    public List<PeopleReportsStore.SavedReport> MyReports { get; set; } = new();
    public List<PeopleReportsStore.SavedReport> SharedReports { get; set; } = new();

    /// <summary>
    /// الموديول المستنتج من المسار: نفس الصفحة تخدم «/PeopleReports» (أشخاص) و
    /// «/AttendanceReports» (حضور) عبر AddPageRoute، فتعرض كلٌّ مصادرها وتقاريرها فقط.
    /// </summary>
    public string Module =>
        (HttpContext?.Request?.Path.Value ?? "").Contains("attendancereports", StringComparison.OrdinalIgnoreCase)
            ? "attendance" : "people";

    public bool IsAttendance => Module == "attendance";

    /// <summary>المسار الأساس للصفحة الحالية — لروابط GET وإجراءات POST وإعادة التوجيه.</summary>
    public string SelfPath => IsAttendance ? "/AttendanceReports" : "/PeopleReports";

    public string PageTitle => IsAttendance ? "تقارير الحضور" : "التقارير";

    public IReadOnlyList<PeopleReportCatalog.ReportDataset> Datasets => PeopleReportCatalog.DatasetsFor(Module);

    public List<CompanyOption> Companies { get; set; } = new();

    // مستخدمو النظام لخيار «مشاركة مع أشخاص محددين» بالباني.
    public List<string> ShareUserOptions { get; set; } = new();

    // ---- Run result ----
    public PeopleReportsStore.SavedReport? Current { get; set; }
    public List<PeopleReportCatalog.ReportColumn> RunColumns { get; set; } = new();
    public List<Dictionary<string, string>> RunRows { get; set; } = new();

    // مرشحات التقرير (المختارة بالباني) + قيمها الحالية من الـ query string.
    // المفاتيح بـ RunFilterValues: <key> للنص/القائمة، و<key>_from / <key>_to لنطاق التاريخ.
    public List<PeopleReportCatalog.ReportColumn> RunFilterColumns { get; set; } = new();
    public Dictionary<string, string> RunFilterValues { get; set; } = new();
    public Dictionary<string, List<string>> RunFilterOptions { get; set; } = new();

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
                return Redirect(SelfPath);
            }

            await RunAsync(Current);
        }

        return Page();
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var d) ? d : null;

    public async Task<IActionResult> OnGetExportAsync()
    {
        var report = await PeopleReportsStore.GetAsync(_dbContext, ReportId);
        if (report == null)
        {
            return Redirect(SelfPath);
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
        string name, string? description, string datasetKey, string columnsCsv, string visibility,
        int id = 0, string? filterColumnsCsv = null, List<string>? sharedWith = null)
    {
        await PeopleReportsStore.EnsureSchemaAsync(_dbContext);

        var dataset = PeopleReportCatalog.GetDataset(datasetKey ?? "");
        name = (name ?? "").Trim();

        if (dataset == null || string.IsNullOrWhiteSpace(name))
        {
            Message = "اسم التقرير ومصدر البيانات مطلوبان.";
            return Redirect(SelfPath);
        }

        // Ordered columns come from the picker as CSV; keep only valid keys.
        var validColumns = ValidKeys(columnsCsv, dataset);

        if (validColumns.Count == 0)
        {
            Message = "اختر عموداً واحداً على الأقل.";
            return Redirect(SelfPath);
        }

        var validFilters = ValidKeys(filterColumnsCsv, dataset);

        var isShared = string.Equals(visibility, "everyone", StringComparison.OrdinalIgnoreCase);
        var isSpecific = string.Equals(visibility, "specific", StringComparison.OrdinalIgnoreCase);
        var sharedWithCsv = isSpecific && sharedWith is { Count: > 0 }
            ? string.Join(",", sharedWith.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            : null;

        if (id > 0)
        {
            await PeopleReportsStore.UpdateOwnAsync(
                _dbContext, id, name, description, dataset.Key, string.Join(",", validColumns), CurrentUser, isShared,
                sharedWithCsv, validFilters.Count > 0 ? string.Join(",", validFilters) : null);
            Message = "تم تحديث التقرير.";
        }
        else
        {
            await PeopleReportsStore.CreateAsync(
                _dbContext, name, description, dataset.Key, string.Join(",", validColumns), CurrentUser, isShared,
                sharedWithCsv, validFilters.Count > 0 ? string.Join(",", validFilters) : null);
            Message = "تم حفظ التقرير.";
        }

        return Redirect(SelfPath + "#mine");
    }

    private static List<string> ValidKeys(string? csv, PeopleReportCatalog.ReportDataset dataset) =>
        (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => dataset.Columns.Any(dc => dc.Key.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

    public async Task<IActionResult> OnPostDeleteReportAsync(int id)
    {
        await PeopleReportsStore.DeleteOwnAsync(_dbContext, id, CurrentUser);
        Message = "تم حذف التقرير.";
        return Redirect(SelfPath + "#mine");
    }

    public async Task<IActionResult> OnPostToggleShareAsync(int id)
    {
        await PeopleReportsStore.ToggleShareOwnAsync(_dbContext, id, CurrentUser);
        Message = "تم تحديث المشاركة.";
        return Redirect(SelfPath + "#mine");
    }

    private async Task LoadListsAsync()
    {
        var all = await PeopleReportsStore.LoadAllAsync(_dbContext);

        // اقصر القوائم على مصادر هذا الموديول فقط (أشخاص مقابل حضور).
        var moduleKeys = new HashSet<string>(
            Datasets.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
        all = all.Where(r => moduleKeys.Contains(r.DatasetKey)).ToList();

        SystemReports = all.Where(r => r.IsSystem).ToList();
        MyReports = all.Where(r => !r.IsSystem && string.Equals(r.OwnerUser, CurrentUser, StringComparison.OrdinalIgnoreCase)).ToList();

        // مشاركة مع الجميع (IsShared) أو معي تحديداً (SharedWith).
        SharedReports = all.Where(r =>
            !r.IsSystem &&
            !string.Equals(r.OwnerUser, CurrentUser, StringComparison.OrdinalIgnoreCase) &&
            (r.IsShared || r.SharedWith.Contains(CurrentUser, StringComparer.OrdinalIgnoreCase))).ToList();

        Companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync();

        ShareUserOptions = await _dbContext.SystemUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.UserName != CurrentUser)
            .OrderBy(u => u.UserName)
            .Select(u => u.UserName)
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
                ActiveOnly = ActiveOnly,
                From = ParseDate(From),
                To = ParseDate(To)
            });

        // «البحث المتقدم» نمط كيان: يعرض حصراً مرشحات التقرير المعرّفة —
        // المختارة بالباني للتقارير المخصصة، والمزروعة افتراضياً لتقارير النظام.
        // نص = يحتوي، قائمة = مطابقة تامة (خياراتها القيم الفعلية بالبيانات)، تاريخ = نطاق من/إلى.
        RunFilterColumns = report.FilterColumns
            .Select(key => dataset.Columns.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        foreach (var filterColumn in RunFilterColumns.Where(c => c.Filter == PeopleReportCatalog.FilterKind.Select))
        {
            RunFilterOptions[filterColumn.Key] = RunRows
                .Select(row => row.GetValueOrDefault(filterColumn.Key, ""))
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(v => v)
                .Take(300)
                .ToList();
        }

        foreach (var filterColumn in RunFilterColumns)
        {
            string Q(string suffix) => (Request.Query["cf_" + filterColumn.Key + suffix].ToString() ?? "").Trim();

            switch (filterColumn.Filter)
            {
                case PeopleReportCatalog.FilterKind.DateRange:
                    var from = Q("_from");
                    var to = Q("_to");
                    if (!string.IsNullOrEmpty(from))
                    {
                        RunFilterValues[filterColumn.Key + "_from"] = from;
                        // التواريخ بصيغة yyyy-MM-dd فالمقارنة النصية الترتيبية صحيحة.
                        RunRows = RunRows.Where(row =>
                            string.Compare(row.GetValueOrDefault(filterColumn.Key, ""), from, StringComparison.Ordinal) >= 0).ToList();
                    }
                    if (!string.IsNullOrEmpty(to))
                    {
                        RunFilterValues[filterColumn.Key + "_to"] = to;
                        RunRows = RunRows.Where(row =>
                        {
                            var v = row.GetValueOrDefault(filterColumn.Key, "");
                            return !string.IsNullOrEmpty(v) && string.Compare(v, to, StringComparison.Ordinal) <= 0;
                        }).ToList();
                    }
                    break;

                case PeopleReportCatalog.FilterKind.Select:
                    var selected = Q("");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        RunFilterValues[filterColumn.Key] = selected;
                        RunRows = RunRows.Where(row =>
                            string.Equals(row.GetValueOrDefault(filterColumn.Key, ""), selected, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    break;

                default:
                    var value = Q("");
                    if (!string.IsNullOrEmpty(value))
                    {
                        RunFilterValues[filterColumn.Key] = value;
                        RunRows = RunRows.Where(row =>
                            row.GetValueOrDefault(filterColumn.Key, "").Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    break;
            }
        }
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
