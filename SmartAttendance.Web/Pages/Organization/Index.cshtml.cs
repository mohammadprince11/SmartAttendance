using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Organization;

/// <summary>
/// الهياكل التنظيمية بثلاثة تبويبات (نمط كيان): هيكلية الشركة (فروع/أقسام) +
/// الهيكل الهرمي (شجرة المدراء عبر OrgChartBuilder) + الهيكل الوظيفي (المناصب).
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
    public string? Search { get; set; }

    // ---- Hierarchical (chart) tab ----
    [BindProperty(SupportsGet = true)]
    public int? ChartCompanyId { get; set; }

    public List<ChartCompanyOption> ChartCompanies { get; set; } = new();

    public string ChartCompanyName { get; set; } = string.Empty;

    public OrgChartData Chart { get; set; } = new();

    // ---- Functional (positions) tab ----
    public List<PositionRow> Positions { get; set; } = new();

    public List<CompanyViewModel> Companies { get; set; } = new();

    public int TotalCompanies { get; set; }

    public int TotalBranches { get; set; }

    public int TotalDepartments { get; set; }

    public int TotalEmployees { get; set; }

    public int ActiveEmployees { get; set; }

    public int InactiveEmployees { get; set; }

    [BindProperty]
    public CompanyInputModel CompanyInput { get; set; } = new();

    [BindProperty]
    public BranchInputModel BranchInput { get; set; } = new();

    [BindProperty]
    public DepartmentInputModel DepartmentInput { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        await LoadAsync(scope);
        await LoadChartAsync(scope);
        await LoadPositionsAsync(scope);
    }

    private async Task LoadChartAsync(CompanyScope scope)
    {
        // المُنتقي محصورٌ بشركات المستخدم؛ ودورٌ مقيَّد لا يرى إلا شركاته. لذا
        // `Resolve` أدناه يرفض أي ChartCompanyId مُمرَّر بالرابط خارج النطاق، فلا
        // يُبنى هيكل شركةٍ أجنبية (أسماء الموظفين وسلسلة المدراء).
        ChartCompanies = (await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new ChartCompanyOption { Id = x.Id, Name = x.Name })
            .ToListAsync())
            .Where(x => scope.Allows(x.Id))
            .ToList();

        ChartCompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            ChartCompanyId,
            ChartCompanies.Select(x => x.Id).ToArray());

        // حارس دفاعيّ صريح: لا نبني هيكلاً إلا لشركةٍ يسمح بها النطاق (Resolve يضمنها
        // أصلاً بحصر القائمة، وهذا يجعل الخاصّية الأمنية محلّيةً واضحة).
        if (!ChartCompanyId.HasValue || !scope.Allows(ChartCompanyId.Value))
        {
            ChartCompanyId = null;
            return;
        }

        ChartCompanyName = ChartCompanies
            .FirstOrDefault(x => x.Id == ChartCompanyId.Value)?.Name ?? string.Empty;

        Chart = await OrgChartBuilder.BuildAsync(_dbContext, ChartCompanyId.Value);
    }

    private async Task LoadPositionsAsync(CompanyScope scope)
    {
        // عدّ الموظفين لكل منصب محصورٌ بالشركات المسموحة حتى لا تتسرّب أعداد شركةٍ أخرى.
        var employeesQuery = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.PositionId != null);

        employeesQuery = ApplyCompanyScope(employeesQuery, scope);

        var counts = await employeesQuery
            .GroupBy(e => e.PositionId!.Value)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PositionId, x => x.Count);

        Positions = await _dbContext.HrJobPositions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.ArabicName)
            .Select(p => new PositionRow
            {
                Id = p.Id,
                Name = p.ArabicName,
                EnglishName = p.EnglishName
            })
            .ToListAsync();

        foreach (var position in Positions)
        {
            position.EmployeeCount = counts.TryGetValue(position.Id, out var c) ? c : 0;
        }

        Positions = Positions
            .OrderByDescending(p => p.EmployeeCount)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateCompanyAsync()
    {
        // إنشاء شركةٍ جديدة = إنشاء حدّ استئجارٍ جديد؛ لا يُسمح به إلا لغير المقيَّد
        // (الأدمن). دورٌ مقيَّد بشركات لا يخلق كياناً خارج نطاقه.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsUnrestricted)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(CompanyInput.Name))
        {
            ErrorMessage = "اسم الشركة مطلوب.";
            return RedirectToPage();
        }

        var code = NormalizeCode(CompanyInput.Code, CompanyInput.Name);

        var exists = await _dbContext.Companies
            .AnyAsync(x => x.Code == code || x.Name == CompanyInput.Name.Trim());

        if (exists)
        {
            ErrorMessage = "الشركة موجودة مسبقاً بنفس الاسم أو الكود.";
            return RedirectToPage();
        }

        var company = new Company
        {
            Name = CompanyInput.Name.Trim(),
            Code = code,
            IsActive = CompanyInput.IsActive
        };

        _dbContext.Companies.Add(company);
        await _dbContext.SaveChangesAsync();

        SuccessMessage = "تمت إضافة الشركة بنجاح.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateBranchAsync()
    {
        if (BranchInput.CompanyId <= 0 || string.IsNullOrWhiteSpace(BranchInput.Name))
        {
            ErrorMessage = "بيانات الفرع غير مكتملة.";
            return RedirectToPage();
        }

        // لا نعتمد CompanyId القادم من النموذج بلا فحص: الفرع لا يُضاف إلا لشركةٍ
        // ضمن نطاق المستخدم، وإلا كان كتابةً عابرة للشركات.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.Allows(BranchInput.CompanyId))
        {
            return NotFound();
        }

        var companyExists = await _dbContext.Companies
            .AnyAsync(x => x.Id == BranchInput.CompanyId);

        if (!companyExists)
        {
            ErrorMessage = "الشركة المحددة غير موجودة.";
            return RedirectToPage();
        }

        var code = NormalizeCode(BranchInput.Code, BranchInput.Name);

        var exists = await _dbContext.Branches
            .AnyAsync(x => x.CompanyId == BranchInput.CompanyId && (x.Code == code || x.Name == BranchInput.Name.Trim()));

        if (exists)
        {
            ErrorMessage = "الفرع موجود مسبقاً داخل نفس الشركة بنفس الاسم أو الكود.";
            return RedirectToPage();
        }

        var branch = new Branch
        {
            CompanyId = BranchInput.CompanyId,
            Name = BranchInput.Name.Trim(),
            Code = code,
            Address = string.IsNullOrWhiteSpace(BranchInput.Address) ? null : BranchInput.Address.Trim(),
            IsActive = BranchInput.IsActive
        };

        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        SuccessMessage = "تمت إضافة الفرع بنجاح.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateDepartmentAsync()
    {
        if (DepartmentInput.BranchId <= 0 || string.IsNullOrWhiteSpace(DepartmentInput.Name))
        {
            ErrorMessage = "بيانات القسم غير مكتملة.";
            return RedirectToPage();
        }

        // القسم يُعلَّق على فرع؛ ونطاق الكتابة يُحسم من شركة ذلك الفرع لا من مدخل
        // النموذج — فلا يُضاف قسمٌ لفرع شركةٍ خارج نطاق المستخدم.
        var branchCompanyId = await _dbContext.Branches
            .Where(x => x.Id == DepartmentInput.BranchId)
            .Select(x => (int?)x.CompanyId)
            .FirstOrDefaultAsync();

        if (branchCompanyId is null)
        {
            ErrorMessage = "الفرع المحدد غير موجود.";
            return RedirectToPage();
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.Allows(branchCompanyId.Value))
        {
            return NotFound();
        }

        var code = NormalizeCode(DepartmentInput.Code, DepartmentInput.Name);

        var exists = await _dbContext.Departments
            .AnyAsync(x => x.BranchId == DepartmentInput.BranchId && (x.Code == code || x.Name == DepartmentInput.Name.Trim()));

        if (exists)
        {
            ErrorMessage = "القسم موجود مسبقاً داخل نفس الفرع بنفس الاسم أو الكود.";
            return RedirectToPage();
        }

        var department = new Department
        {
            BranchId = DepartmentInput.BranchId,
            Name = DepartmentInput.Name.Trim(),
            Code = code,
            IsActive = DepartmentInput.IsActive
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync();

        SuccessMessage = "تمت إضافة القسم بنجاح.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CompanyScope scope)
    {
        // كل عدّ موظفين يمرّ بمرشّح النطاق: لغير المقيَّد يعيده كما هو (سلوك الأدمن
        // ثابت)، وللمقيَّد يقصره على شركاته فلا تتسرّب أعداد شركةٍ أخرى.
        var scopedEmployees = ApplyCompanyScope(
            _dbContext.Employees.AsNoTracking(),
            scope);

        TotalEmployees = await scopedEmployees.CountAsync();
        ActiveEmployees = await scopedEmployees.CountAsync(x => x.IsActive);
        InactiveEmployees = TotalEmployees - ActiveEmployees;

        // الشركات والفروع المعروضة محصورةٌ بنطاق المستخدم؛ فلا هيكل شركةٍ أجنبية.
        var companyRows = (await _dbContext.Companies
            .AsNoTracking()
            .Select(x => new CompanyViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code,
                IsActive = x.IsActive
            })
            .OrderBy(x => x.Name)
            .ToListAsync())
            .Where(x => scope.Allows(x.Id))
            .ToList();

        var branchRows = (await _dbContext.Branches
            .AsNoTracking()
            .Select(x => new BranchViewModel
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                Name = x.Name,
                Code = x.Code,
                Address = x.Address,
                IsActive = x.IsActive
            })
            .OrderBy(x => x.Name)
            .ToListAsync())
            .Where(x => scope.Allows(x.CompanyId))
            .ToList();

        // الأقسام كيانٌ مرجعيّ مشترك (بلا CompanyId)؛ تُستخدم قائمةً للأسماء فقط،
        // والانتماء الشركويّ يُشتقّ من فرع الموظف داخل النطاق أدناه.
        var departmentRows = await _dbContext.Departments
            .AsNoTracking()
            .Select(x => new DepartmentViewModel
            {
                Id = x.Id,
                BranchId = x.BranchId ?? 0,
                Name = x.Name,
                Code = x.Code,
                IsActive = x.IsActive
            })
            .OrderBy(x => x.Name)
            .ToListAsync();

        // الموظف مرتبط بالفرع (موقع العمل) مباشرةً عبر BranchId، وبالقسم مستقلاً؛ والأقسام
        // مشتركة بين الفروع (Departments.BranchId فارغ) فلا يصح اشتقاق فرع الموظف من قسمه.
        // نحسب الموظفين لكل (فرع، قسم) من فرع الموظف المباشر — ضمن النطاق.
        var branchDeptCounts = await ApplyCompanyScope(
                _dbContext.Employees.AsNoTracking(),
                scope)
            .GroupBy(x => new { x.BranchId, x.DepartmentId })
            .Select(g => new { g.Key.BranchId, g.Key.DepartmentId, Count = g.Count() })
            .ToListAsync();

        var employeeCountsByBranch = branchDeptCounts
            .GroupBy(x => x.BranchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var employeeCountsByDepartment = await ApplyCompanyScope(
                _dbContext.Employees.AsNoTracking(),
                scope)
            .GroupBy(x => x.DepartmentId)
            .Select(x => new
            {
                DepartmentId = x.Key,
                Count = x.Count()
            })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

        // البطاقات الإجمالية: لغير المقيَّد أعدادٌ عامّة كما كانت؛ وللمقيَّد أعدادٌ من
        // شركاته وحدها (الأقسام = المتميّزة الظاهرة لموظفيه ضمن النطاق).
        if (scope.IsUnrestricted)
        {
            TotalCompanies = await _dbContext.Companies.CountAsync();
            TotalBranches = await _dbContext.Branches.CountAsync();
            TotalDepartments = await _dbContext.Departments.CountAsync();
        }
        else
        {
            TotalCompanies = companyRows.Count;
            TotalBranches = branchRows.Count;
            TotalDepartments = branchDeptCounts
                .Select(x => x.DepartmentId)
                .Distinct()
                .Count();
        }

        foreach (var department in departmentRows)
        {
            department.EmployeeCount = employeeCountsByDepartment.TryGetValue(department.Id, out var employees)
                ? employees
                : 0;
        }

        var departmentsById = departmentRows.ToDictionary(x => x.Id);

        foreach (var branch in branchRows)
        {
            // أقسام الفرع = الأقسام التي لموظفيه فعلاً، وعدد موظفي كل قسم داخل هذا الفرع.
            branch.Departments = branchDeptCounts
                .Where(x => x.BranchId == branch.Id && departmentsById.ContainsKey(x.DepartmentId))
                .Select(x =>
                {
                    var d = departmentsById[x.DepartmentId];
                    return new DepartmentViewModel
                    {
                        Id = d.Id,
                        BranchId = branch.Id,
                        Name = d.Name,
                        Code = d.Code,
                        IsActive = d.IsActive,
                        EmployeeCount = x.Count
                    };
                })
                .OrderBy(x => x.Name)
                .ToList();

            branch.DepartmentCount = branch.Departments.Count;

            branch.EmployeeCount = employeeCountsByBranch.TryGetValue(branch.Id, out var branchEmployees)
                ? branchEmployees
                : 0;
        }

        foreach (var company in companyRows)
        {
            company.Branches = branchRows
                .Where(x => x.CompanyId == company.Id)
                .ToList();

            company.BranchCount = company.Branches.Count;
            company.DepartmentCount = company.Branches.Sum(x => x.DepartmentCount);
            company.EmployeeCount = company.Branches.Sum(x => x.EmployeeCount);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();

            companyRows = companyRows
                .Where(company =>
                    Contains(company.Name, term) ||
                    Contains(company.Code, term) ||
                    company.Branches.Any(branch =>
                        Contains(branch.Name, term) ||
                        Contains(branch.Code, term) ||
                        branch.Departments.Any(department =>
                            Contains(department.Name, term) ||
                            Contains(department.Code, term))))
                .ToList();
        }

        Companies = companyRows;
    }

    /// <summary>
    /// يقصر استعلام الموظفين على شركات النطاق: غير المقيَّد كما هو، والمرفوض كلّياً
    /// لا شيء، وإلا الشركات المسموحة فقط (الموظف بلا شركة يُستبعَد للمقيَّد).
    /// </summary>
    private static IQueryable<Employee> ApplyCompanyScope(
        IQueryable<Employee> query,
        CompanyScope scope)
    {
        if (scope.IsUnrestricted)
        {
            return query;
        }

        if (scope.IsDeniedAll)
        {
            return query.Where(_ => false);
        }

        var allowed = scope.AllowedCompanyIds.ToList();
        return query.Where(e =>
            e.CompanyId != null && allowed.Contains(e.CompanyId.Value));
    }

    private static string NormalizeCode(string? code, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim();
        }

        var value = new string(fallback
            .Where(char.IsLetterOrDigit)
            .Take(12)
            .ToArray());

        return string.IsNullOrWhiteSpace(value)
            ? Guid.NewGuid().ToString("N")[..8]
            : value.ToUpperInvariant();
    }

    private static bool Contains(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public class CompanyInputModel
    {
        public string Name { get; set; } = string.Empty;

        public string? Code { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class BranchInputModel
    {
        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Code { get; set; }

        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class DepartmentInputModel
    {
        public int BranchId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Code { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class CompanyViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int BranchCount { get; set; }

        public int DepartmentCount { get; set; }

        public int EmployeeCount { get; set; }

        public List<BranchViewModel> Branches { get; set; } = new();
    }

    public class BranchViewModel
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string? Address { get; set; }

        public bool IsActive { get; set; }

        public int DepartmentCount { get; set; }

        public int EmployeeCount { get; set; }

        public List<DepartmentViewModel> Departments { get; set; } = new();
    }

    public class DepartmentViewModel
    {
        public int Id { get; set; }

        public int BranchId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int EmployeeCount { get; set; }
    }

    public class ChartCompanyOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class PositionRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? EnglishName { get; set; }

        public int EmployeeCount { get; set; }
    }
}
