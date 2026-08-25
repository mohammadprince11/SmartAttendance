using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Reports;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.PeopleReports;

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
    public string Module
    {
        get
        {
            var path = HttpContext?.Request?.Path.Value ?? "";
            if (path.Contains("attendancereports", StringComparison.OrdinalIgnoreCase)) return "attendance";
            if (path.Contains("payrollreports", StringComparison.OrdinalIgnoreCase)) return "payroll";
            return "people";
        }
    }

    public bool IsAttendance => Module == "attendance";
    public bool IsPayroll => Module == "payroll";

    /// <summary>الموديولات ذات بارامتر مدى تاريخ عند التشغيل (الحضور والرواتب).</summary>
    public bool UsesDateRange => IsAttendance || IsPayroll;

    /// <summary>المسار الأساس للصفحة الحالية — لروابط GET وإجراءات POST وإعادة التوجيه.</summary>
    public string SelfPath => IsAttendance ? "/AttendanceReports" : IsPayroll ? "/PayrollReports" : "/PeopleReports";

    public string PageTitle => IsAttendance ? "تقارير الحضور" : IsPayroll ? "تقارير الرواتب" : "التقارير";

    public IReadOnlyList<PeopleReportCatalog.ReportDataset> Datasets { get; private set; } = Array.Empty<PeopleReportCatalog.ReportDataset>();

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
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        if (CompanyId.HasValue && !scope.Allows(CompanyId.Value)) return Forbid();
        await LoadListsAsync();

        if (ReportId > 0)
        {
            Current = await PeopleReportsStore.GetAsync(_dbContext, scope, ReportId);
            if (Current == null || !IsDatasetAllowed(Current.DatasetKey))
            {
                return Redirect(SelfPath);
            }

            await RunAsync(Current);
        }

        return Page();
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var d) ? d : null;

    /// <summary>
    /// عدّادات بطاقات التقارير (نمط كيان: كل بطاقة عليها عدّاد) — تُطلب **بعد**
    /// رسم الصفحة لا معها.
    ///
    /// ⚠️ العدّ الفوريّ كان سيعني تشغيل كل تقرير عند كل فتحة للصفحة: عشرات
    /// الاستعلامات على 1356 موظفاً قبل ظهور أول بطاقة. فالعدّ كسول، و**مجمَّع
    /// بمفتاح (المصدر، المرشّح)**: تقريران يختلفان بالأعمدة فقط يقرآن نفس الصفوف
    /// مرّة واحدة — عشرون تقريراً غالباً أقلّ من عشرة استعلامات.
    /// </summary>
    public async Task<IActionResult> OnGetCountsAsync()
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        if (CompanyId.HasValue && !scope.Allows(CompanyId.Value)) return Forbid();
        await LoadListsAsync();

        var reports = SystemReports.Concat(MyReports).Concat(SharedReports).ToList();
        var bySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>();

        foreach (var report in reports)
        {
            var reportCompanyId = report.CompanyId ?? CompanyId;
            var sourceKey = report.DatasetKey + "|" + (report.FilterKey ?? "") + "|" + reportCompanyId;

            if (!bySource.TryGetValue(sourceKey, out var count))
            {
                var rows = await PeopleReportCatalog.LoadAsync(
                    _dbContext, report.DatasetKey, report.FilterKey,
                    new PeopleReportCatalog.ReportFilters
                    {
                        Scope = scope,
                        CompanyId = reportCompanyId,
                        ActiveOnly = ActiveOnly,
                        From = ParseDate(From),
                        To = ParseDate(To)
                    });
                count = rows.Count;
                bySource[sourceKey] = count;
            }

            counts[report.Id.ToString()] = count;
        }

        return new JsonResult(counts);
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        if (CompanyId.HasValue && !scope.Allows(CompanyId.Value)) return Forbid();
        var report = await PeopleReportsStore.GetAsync(_dbContext, scope, ReportId);
        if (report == null || !IsDatasetAllowed(report.DatasetKey))
        {
            return Redirect(SelfPath);
        }

        await RunAsync(report);

        var export = ReportExportService.Build(
            format, report.Name,
            RunColumns.Select(column => new ReportExportService.Column(column.Key, column.Label)).ToList(),
            RunRows);
        return File(export.Bytes, export.ContentType, $"report-{report.Id}.{export.Extension}");
    }

    public async Task<IActionResult> OnPostCreateReportAsync(
        string name, string? description, string datasetKey, string columnsCsv, string visibility,
        int companyId, int id = 0, string? filterColumnsCsv = null, List<string>? sharedWith = null, bool shareWithEmployees = false)
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        await PeopleReportsStore.EnsureSchemaAsync(_dbContext);
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();

        var dataset = PeopleReportCatalog.GetDataset(datasetKey ?? "");
        name = (name ?? "").Trim();

        if (dataset == null || !IsDatasetAllowed(dataset.Key) || string.IsNullOrWhiteSpace(name))
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
        var allowedShareUsers = await AllowedShareUsersAsync(scope, companyId);
        var sharedWithCsv = isSpecific && sharedWith is { Count: > 0 }
            ? string.Join(",", sharedWith.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim())
                .Where(allowedShareUsers.Contains).Distinct(StringComparer.OrdinalIgnoreCase))
            : null;

        if (id > 0)
        {
            await PeopleReportsStore.UpdateOwnAsync(
                _dbContext, scope, companyId, id, name, description, dataset.Key, string.Join(",", validColumns), CurrentUser, isShared,
                sharedWithCsv, validFilters.Count > 0 ? string.Join(",", validFilters) : null, shareWithEmployees);
            Message = "تم تحديث التقرير.";
        }
        else
        {
            await PeopleReportsStore.CreateAsync(
                _dbContext, scope, companyId, name, description, dataset.Key, string.Join(",", validColumns), CurrentUser, isShared,
                sharedWithCsv, validFilters.Count > 0 ? string.Join(",", validFilters) : null, shareWithEmployees);
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

    /// <summary>
    /// «أنشئ نسخة» — نمط كيان المتكرّر بكل شاشاته: الاستنساخ بديلٌ عن البناء من
    /// الصفر. ويعمل على **تقارير النظام أيضاً** وهو أهمّ استعمالاته: تأخذ تقريراً
    /// جاهزاً وتعدّل أعمدته بدل أن تبنيه حقلاً حقلاً.
    ///
    /// النسخة تُولَد **مملوكةً لك وغير مشاركة** مهما كان الأصل — مشاركةُ الأصل
    /// قرارُ صاحبه لا يُورَّث بالنسخ.
    /// </summary>
    public async Task<IActionResult> OnPostDuplicateReportAsync(int id, int? companyId)
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        var source = await PeopleReportsStore.GetAsync(_dbContext, scope, id);
        if (source == null || !IsDatasetAllowed(source.DatasetKey))
        {
            Message = "التقرير غير موجود.";
            return Redirect(SelfPath);
        }

        var targetCompanyId = companyId ?? source.CompanyId;
        if (targetCompanyId is not > 0 || !scope.Allows(targetCompanyId))
        {
            Message = "اختر شركة من مرشح الصفحة قبل إنشاء نسخة.";
            return Redirect(SelfPath);
        }

        // تحميل القوائم لازم قبل تسمية النسخة: NextCopyName يقرأ أسماء تقاريري
        // ليتجنّب التكرار، وهي فارغة بمسار POST ما لم تُحمَّل.
        await LoadListsAsync();

        await PeopleReportsStore.CreateAsync(
            _dbContext,
            scope,
            targetCompanyId.Value,
            NextCopyName(source.Name),
            source.Description,
            source.DatasetKey,
            source.ColumnsCsv,
            CurrentUser,
            isShared: false,
            sharedWithCsv: null,
            filterColumnsCsv: source.FilterColumnsCsv);

        Message = "تم إنشاء نسخة بتبويب «تقاريري».";
        return Redirect(SelfPath + "#mine");
    }

    /// <summary>
    /// اسم النسخة: «س — نسخة»، ثم «(2)» فما فوق عند التكرار. بلا ترقيم يصير
    /// عند المستخدم خمسة تقارير بنفس الاسم فلا يميّزها.
    /// </summary>
    private string NextCopyName(string sourceName)
    {
        const string suffix = " — نسخة";
        var baseName = sourceName.EndsWith(suffix, StringComparison.Ordinal)
            ? sourceName
            : sourceName + suffix;

        var taken = MyReports.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} ({DateTime.Now:HHmmss})";
    }

    public async Task<IActionResult> OnPostDeleteReportAsync(int id)
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        var report = await PeopleReportsStore.GetAsync(_dbContext, scope, id);
        if (report == null || !IsDatasetAllowed(report.DatasetKey)) return Forbid();
        await PeopleReportsStore.DeleteOwnAsync(_dbContext, scope, id, CurrentUser);
        Message = "تم حذف التقرير.";
        return Redirect(SelfPath + "#mine");
    }

    public async Task<IActionResult> OnPostToggleShareAsync(int id)
    {
        if (!await LoadReportAccessAsync()) return Forbid();
        var scope = await _companyScope.GetAsync();
        var report = await PeopleReportsStore.GetAsync(_dbContext, scope, id);
        if (report == null || !IsDatasetAllowed(report.DatasetKey)) return Forbid();
        await PeopleReportsStore.ToggleShareOwnAsync(_dbContext, scope, id, CurrentUser);
        Message = "تم تحديث المشاركة.";
        return Redirect(SelfPath + "#mine");
    }

    private async Task LoadListsAsync()
    {
        var scope = await _companyScope.GetAsync();
        var all = await PeopleReportsStore.LoadAllAsync(_dbContext, scope);

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

        var companyQuery = _dbContext.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive);
        if (!scope.IsUnrestricted)
        {
            var allowedCompanyIds = scope.AllowedCompanyIds.ToArray();
            companyQuery = companyQuery.Where(c => allowedCompanyIds.Contains(c.Id));
        }
        Companies = await companyQuery
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync();

        ShareUserOptions = (await AllowedShareUsersAsync(scope, CompanyId)).OrderBy(u => u).ToList();
    }

    private async Task<HashSet<string>> AllowedShareUsersAsync(CompanyScope scope, int? companyId)
    {
        var query = _dbContext.SystemUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.UserName != CurrentUser &&
                        u.EmployeeId != null && u.Employee != null && u.Employee.CompanyId != null);

        if (companyId is > 0)
        {
            if (!scope.Allows(companyId)) return new(StringComparer.OrdinalIgnoreCase);
            query = query.Where(u => u.Employee!.CompanyId == companyId);
        }
        else if (!scope.IsUnrestricted)
        {
            var allowedCompanyIds = scope.AllowedCompanyIds.ToArray();
            query = query.Where(u => u.Employee!.CompanyId != null && allowedCompanyIds.Contains(u.Employee.CompanyId.Value));
        }

        return (await query.Select(u => u.UserName).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> LoadReportAccessAsync()
    {
        var moduleDatasets = PeopleReportCatalog.DatasetsFor(Module);
        if (RoleRouteCatalog.IsAdmin(PeopleAccessContext.GetRole(HttpContext)))
        {
            Datasets = moduleDatasets;
            return true;
        }

        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        if (systemUserId <= 0) return false;
        var roleCount = await AccessRoleStore.CountUserRolesAsync(
            _dbContext, systemUserId, AccessRoleStore.TypeReports);
        if (roleCount == 0)
        {
            Datasets = moduleDatasets;
            return true;
        }

        var grants = (await AccessRoleStore.GetUserGrantsAsync(
                _dbContext, systemUserId, AccessRoleStore.TypeReports))
            .Select(grant => grant.GrantKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Datasets = moduleDatasets.Where(dataset => grants.Contains(ReportGroupFor(dataset))).ToList();
        return Datasets.Count > 0;
    }

    private bool IsDatasetAllowed(string datasetKey) =>
        Datasets.Any(dataset => dataset.Key.Equals(datasetKey, StringComparison.OrdinalIgnoreCase));

    private static string ReportGroupFor(PeopleReportCatalog.ReportDataset dataset) =>
        dataset.Module.Equals("attendance", StringComparison.OrdinalIgnoreCase) ? "Attendance" :
        dataset.Module.Equals("payroll", StringComparison.OrdinalIgnoreCase) ? "Payroll" :
        dataset.Key.Equals("leaves", StringComparison.OrdinalIgnoreCase) ? "Leaves" :
        "Employees";

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
                Scope = await _companyScope.GetAsync(),
                CompanyId = report.CompanyId ?? CompanyId,
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

    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
