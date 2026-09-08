using SmartAttendance.Web.Infrastructure.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Imports;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class IndexModel : PageModel
{
    private const string ImportFolderName = "Employees";

    private readonly IEmployeeService _employeeService;
    private readonly ICompanyService _companyService;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private readonly SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService _effectiveScopeService;
    private readonly IWebHostEnvironment _environment;
    private readonly EmployeeBootstrapImportEngine _importEngine;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(
        IEmployeeService employeeService,
        ICompanyService companyService,
        IPermissionAuthorizationService permissionAuthorizationService,
        SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService effectiveScopeService,
        IWebHostEnvironment environment,
        ApplicationDbContext dbContext)
    {
        _employeeService = employeeService;
        _companyService = companyService;
        _permissionAuthorizationService = permissionAuthorizationService;
        _effectiveScopeService = effectiveScopeService;
        _environment = environment;
        _importEngine = new EmployeeBootstrapImportEngine(dbContext);
        _dbContext = dbContext;
    }

    public List<EmployeeListViewModel> Employees { get; set; } = new();

    public List<CompanyListViewModel> CompanyOptions { get; set; } = new();

    public List<BranchListViewModel> BranchOptions { get; set; } = new();

    public List<DepartmentListViewModel> DepartmentOptions { get; set; } = new();

    public bool CanCreateEmployee { get; set; }

    public bool CanImportEmployees { get; set; }

    public string SelectedCompanyName =>
        CompanyId.HasValue
            ? CompanyOptions.FirstOrDefault(x =>
                  x.Id == CompanyId.Value)?.Name ?? "-"
            : "-";

    public int TotalEmployees { get; set; }

    public int FilteredEmployees { get; set; }

    public int ActiveEmployees { get; set; }

    public int InactiveEmployees { get; set; }

    public int NewThisYear { get; set; }

    public int TotalPages { get; set; }

    public int FirstItemNumber =>
        FilteredEmployees == 0
            ? 0
            : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber =>
        FilteredEmployees == 0
            ? 0
            : Math.Min(PageNumber * PageSize, FilteredEmployees);

    public IReadOnlyList<int?> PaginationPages =>
        BuildPaginationPages(PageNumber, TotalPages);

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? BranchId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "name";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public async Task OnGetAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        var directoryScope = await _permissionAuthorizationService
            .GetPeopleDataScopeAsync(
                systemUserId,
                PeoplePermissionCodes.ViewDirectory,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.ViewDirectory),
                HttpContext.RequestAborted);

        var accessibleCompanyIds = directoryScope.IsDeniedAll
            ? Array.Empty<int>()
            : (await _employeeService.GetAccessibleCompanyIdsAsync(directoryScope))
                .Concat(directoryScope.AllowedCompanyIds)
                .Except(directoryScope.DeniedCompanyIds)
                .Distinct()
                .ToArray();

        var allCompanies = (await _companyService.GetAllAsync())
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ToList();

        await EmployeeBusinessDataDisplayLocalizer.LocalizeCompaniesAsync(
            _dbContext,
            allCompanies,
            HttpContext.RequestAborted);

        CompanyOptions = directoryScope.IsUnrestricted &&
                         !directoryScope.HasAnyDenial
            ? allCompanies
            : allCompanies
                .Where(x => accessibleCompanyIds.Contains(x.Id))
                .ToList();

        CanCreateEmployee = await _permissionAuthorizationService
            .HasGlobalPermissionAsync(
                systemUserId,
                PeoplePermissionCodes.Create,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.Create),
                HttpContext.RequestAborted);

        CanImportEmployees = await HasImportPermissionAsync();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        SearchTerm = string.IsNullOrWhiteSpace(SearchTerm)
            ? null
            : SearchTerm.Trim();
        StatusFilter = NormalizeStatusFilter(StatusFilter);
        SortBy = NormalizeSortBy(SortBy);
        PageNumber = Math.Max(PageNumber, 1);
        PageSize = NormalizePageSize(PageSize);

        if (!CompanyId.HasValue)
        {
            Employees = new List<EmployeeListViewModel>();
            BranchOptions = new List<BranchListViewModel>();
            DepartmentOptions = new List<DepartmentListViewModel>();
            return;
        }

        BranchOptions = (await _employeeService
                .GetBranchesForDropdownAsync(
                    CompanyId.Value,
                    directoryScope))
            .OrderBy(x => x.Name)
            .ToList();

        await EmployeeBusinessDataDisplayLocalizer.LocalizeBranchesAsync(
            _dbContext,
            BranchOptions,
            HttpContext.RequestAborted);

        BranchOptions = BranchOptions
            .OrderBy(item => item.Name)
            .ToList();

        DepartmentOptions = (await _employeeService
                .GetDepartmentsForDropdownAsync(
                    CompanyId.Value,
                    directoryScope))
            .OrderBy(x => x.Name)
            .ToList();

        await EmployeeBusinessDataDisplayLocalizer.LocalizeDepartmentsAsync(
            _dbContext,
            DepartmentOptions,
            HttpContext.RequestAborted);

        DepartmentOptions = DepartmentOptions
            .OrderBy(item => item.Name)
            .ToList();

        BranchId = BranchId.HasValue &&
                   BranchOptions.Any(x => x.Id == BranchId.Value)
            ? BranchId
            : null;

        DepartmentId = DepartmentId.HasValue &&
                       DepartmentOptions.Any(x =>
                           x.Id == DepartmentId.Value)
            ? DepartmentId
            : null;

        // Unify the Access Roles Employees data scope with the rules-based scope:
        // a row is viewable only if BOTH allow it. Admin, no Data role, or an
        // "All" scope resolve to Unrestricted, so this can only tighten.
        // P0-1 — تمريره لـGetPagedAsync يحصر **عضوية القائمة** بالتقاطع بمستوى SQL،
        // لا بوّابة رابط الملف صفّاً-صفّاً فقط؛ وإلا ظهرت أسطر موظفين خارج نطاق أدواره.
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId,
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase),
            HttpContext.RequestAborted);

        var result = await _employeeService.GetPagedAsync(
            new EmployeeListQueryViewModel
            {
                DataScope = directoryScope,
                AccessRoleScope = accessRoleScope,
                CompanyId = CompanyId,
                SearchTerm = SearchTerm,
                BranchId = BranchId,
                DepartmentId = DepartmentId,
                StatusFilter = StatusFilter,
                SortBy = SortBy,
                PageNumber = PageNumber,
                PageSize = PageSize
            });

        await EmployeeBusinessDataDisplayLocalizer.LocalizeEmployeeListAsync(
            _dbContext,
            result.Items,
            HttpContext.RequestAborted);

        var profileScope = await _permissionAuthorizationService
            .GetPeopleDataScopeAsync(
                systemUserId,
                PeoplePermissionCodes.ViewProfile,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.ViewProfile),
                HttpContext.RequestAborted);

        foreach (var employee in result.Items)
        {
            employee.CanViewProfile = profileScope.AllowsEmployee(
                    employee.Id,
                    employee.CompanyId,
                    employee.BranchId,
                    employee.DepartmentId)
                && accessRoleScope.AllowsEmployee(
                    employee.Id,
                    employee.CompanyId,
                    employee.BranchId,
                    employee.DepartmentId);
        }

        Employees = result.Items;
        TotalEmployees = result.TotalEmployees;
        FilteredEmployees = result.FilteredEmployees;
        ActiveEmployees = result.ActiveEmployees;
        InactiveEmployees = result.InactiveEmployees;
        NewThisYear = result.NewThisYear;
        PageNumber = result.PageNumber;
        PageSize = result.PageSize;
        TotalPages = result.TotalPages;
    }

    /// <summary>القالب الفارغ: ترويسة الأعمدة وقوائم المراجع فقط.</summary>
    public async Task<IActionResult> OnGetTemplateAsync()
    {
        if (!await HasImportPermissionAsync())
        {
            return Forbid();
        }

        return BuildTemplateFile(
            await _importEngine.BuildTemplateWorkbookAsync(
                companyId: CompanyId),
            "Zynora_Employees_Template");
    }

    /// <summary>القالب نفسه معبّأً بالموظفين الحاليين — ضمن نطاق المستخدم فقط.</summary>
    public async Task<IActionResult> OnGetTemplateDataAsync()
    {
        if (!await HasImportPermissionAsync())
        {
            return Forbid();
        }

        // P0-4 — تصدير البيانات كان يخرج **كل الموظفين** ومعهم الراتب الأساسي بلا
        // نطاق ولا حارس تعويض. الآن يُحصر بمعرّفات نطاق المستخدم، ويُفرَّغ عمود
        // الراتب لمن لا يملك ViewCompensation.
        var exportScope = await BuildExportScopeAsync();

        return BuildTemplateFile(
            await _importEngine.BuildTemplateWorkbookAsync(
                includeData: true,
                exportScope,
                CompanyId),
            "Zynora_Employees_Data");
    }

    /// <summary>
    /// يحسب نطاق التصدير: تقاطع نطاق القواعد (ViewDirectory) مع نطاق أدوار الوصول
    /// لتحديد الموظفين المسموح بتصديرهم، وصلاحية ViewCompensation لعمود الراتب.
    /// </summary>
    private async Task<EmployeeBootstrapImportEngine.TemplateExportScope> BuildExportScopeAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        var includeSalary = await _permissionAuthorizationService.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ViewCompensation,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewCompensation),
            HttpContext.RequestAborted);

        var directoryScope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        // حرمانٌ كليّ ⟹ لا صفوف. غير مقيَّد ⟹ null (الجميع). خلافه ⟹ ترشيح بالمعرّفات.
        if (directoryScope.IsDeniedAll || accessRoleScope.IsDeniedAll)
        {
            return new EmployeeBootstrapImportEngine.TemplateExportScope(
                new HashSet<int>(), includeSalary);
        }

        var unrestricted =
            directoryScope.IsUnrestricted && !directoryScope.HasAnyDenial &&
            accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial;

        if (unrestricted)
        {
            return new EmployeeBootstrapImportEngine.TemplateExportScope(
                null, includeSalary);
        }

        var allowedIds = await _importEngine.ResolveAllowedEmployeeIdsAsync(
            directoryScope, accessRoleScope);

        return new EmployeeBootstrapImportEngine.TemplateExportScope(
            allowedIds, includeSalary);
    }

    /// <summary>
    /// يحسب نطاق الكتابة للاستيراد: تقاطع نطاق القواعد (ViewDirectory) مع أدوار الوصول،
    /// وصلاحية EditCompensation لكتابة الراتب. النطاق العام (أدمن/«All») يُبقي الاستيراد
    /// التأسيسي كاملاً؛ المقيَّد يُحصَر بموظفي بنيته القائمة داخل نطاقه (فرضٌ صارم).
    /// </summary>
    private async Task<EmployeeBootstrapImportEngine.ImportScope> BuildImportScopeAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        var canEditCompensation = await _permissionAuthorizationService.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.EditCompensation,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.EditCompensation),
            HttpContext.RequestAborted);

        var directoryScope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        var unrestricted =
            directoryScope.IsUnrestricted && !directoryScope.HasAnyDenial &&
            accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial;

        if (unrestricted)
        {
            return EmployeeBootstrapImportEngine.ImportScope.Unrestricted(canEditCompensation);
        }

        return new EmployeeBootstrapImportEngine.ImportScope(
            IsUnrestricted: false,
            DirectoryScope: directoryScope,
            AccessRoleScope: accessRoleScope,
            CanEditCompensation: canEditCompensation);
    }

    /// <summary>
    /// استيراد بخطوة واحدة: ملفٌ يُرفع فيُنفَّذ فوراً، ثم رسالةٌ وعودة
    /// للقائمة. لا معاينة — القالب نفسه هو ضابط الشكل.
    /// </summary>
    public async Task<IActionResult> OnPostImportAsync(
        IFormFile? importFile)
    {
        if (!await HasImportPermissionAsync())
        {
            return Forbid();
        }

        if (importFile == null || importFile.Length == 0)
        {
            TempData["ErrorMessage"] = "اختر ملفاً أولاً.";
            return RedirectToPage("./Index", new { CompanyId });
        }

        if (importFile.Length > EmployeeBootstrapImportEngine.MaxFileBytes)
        {
            TempData["ErrorMessage"] =
                "حجم الملف يتجاوز الحدّ المسموح (10 ميغابايت).";
            return RedirectToPage("./Index", new { CompanyId });
        }

        var extension = Path
            .GetExtension(importFile.FileName)
            .ToLowerInvariant();

        if (extension is not ".xlsx" and not ".csv")
        {
            TempData["ErrorMessage"] =
                "نوع الملف غير مدعوم — استخدم xlsx أو csv.";
            return RedirectToPage("./Index", new { CompanyId });
        }

        var folder = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "PageImports",
            ImportFolderName);
        Directory.CreateDirectory(folder);

        var originalFileName = Path.GetFileName(importFile.FileName);
        var storedPath = Path.Combine(
            folder,
            $"{Guid.NewGuid():N}_{MakeSafeFileName(originalFileName)}");

        try
        {
            await using (var stream = System.IO.File.Create(storedPath))
            {
                await importFile.CopyToAsync(stream);
            }

            var importScope = await BuildImportScopeAsync();
            var result = await _importEngine.ImportAsync(
                storedPath,
                originalFileName,
                importScope);

            var messageKey =
                result.CreatedCount > 0 || result.UpdatedCount > 0
                    ? "SuccessMessage"
                    : "ErrorMessage";

            TempData[messageKey] = result.Message;
        }
        catch (Exception exception)
        {
            // في Development نُظهر السبب الجذري الحقيقي بدل رسالة EF العامة.
            var root = exception;
            while (root.InnerException is not null)
            {
                root = root.InnerException;
            }

            if (_environment.IsDevelopment())
            {
                Console.Error.WriteLine("EMPLOYEE IMPORT FAILED:");
                Console.Error.WriteLine(exception);
                TempData["ErrorMessage"] = root.Message;
            }
            else
            {
                TempData["ErrorMessage"] =
                    "تعذر استيراد الموظفين بسبب خطأ في حفظ البيانات. راجع سجل النظام.";
            }
        }

        return RedirectToPage("./Index", new { CompanyId });
    }

    private Task<bool> HasImportPermissionAsync()
    {
        var systemUserId =
            PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return _permissionAuthorizationService
            .HasGlobalPermissionAsync(
                systemUserId,
                PeoplePermissionCodes.Import,
                PeopleCompatibilityAccess.IsAllowed(
                    role,
                    PeoplePermissionCodes.Import),
                HttpContext.RequestAborted);
    }

    private static FileContentResult BuildTemplateFile(
        byte[] workbook,
        string namePrefix)
    {
        return new FileContentResult(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName =
                $"{namePrefix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static int NormalizePageSize(int value)
    {
        return value switch
        {
            10 => 10,
            50 => 50,
            100 => 100,
            _ => 25
        };
    }

    private static string NormalizeStatusFilter(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "inactive" => "inactive",
            _ => "active"
        };
    }

    private static string NormalizeSortBy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "code" => "code",
            "branch" => "branch",
            "department" => "department",
            "hiredate" => "hiredate",
            "status" => "status",
            _ => "name"
        };
    }

    private static IReadOnlyList<int?> BuildPaginationPages(
        int currentPage,
        int totalPages)
    {
        if (totalPages <= 0)
        {
            return Array.Empty<int?>();
        }

        if (totalPages <= 7)
        {
            return Enumerable
                .Range(1, totalPages)
                .Select(x => (int?)x)
                .ToList();
        }

        var pages = new List<int?> { 1 };

        if (currentPage > 4)
        {
            pages.Add(null);
        }

        var start = Math.Max(2, currentPage - 1);
        var end = Math.Min(totalPages - 1, currentPage + 1);

        if (currentPage <= 4)
        {
            start = 2;
            end = 5;
        }
        else if (currentPage >= totalPages - 3)
        {
            start = totalPages - 4;
            end = totalPages - 1;
        }

        for (var pageNumber = start;
             pageNumber <= end;
             pageNumber++)
        {
            pages.Add(pageNumber);
        }

        if (currentPage < totalPages - 3)
        {
            pages.Add(null);
        }

        pages.Add(totalPages);
        return pages;
    }
}
