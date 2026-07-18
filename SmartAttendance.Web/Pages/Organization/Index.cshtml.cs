using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Pages.Organization;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
        await LoadAsync();
        await LoadChartAsync();
        await LoadPositionsAsync();
    }

    private async Task LoadChartAsync()
    {
        ChartCompanies = await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new ChartCompanyOption { Id = x.Id, Name = x.Name })
            .ToListAsync();

        ChartCompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            ChartCompanyId,
            ChartCompanies.Select(x => x.Id).ToArray());

        if (!ChartCompanyId.HasValue)
        {
            return;
        }

        ChartCompanyName = ChartCompanies
            .FirstOrDefault(x => x.Id == ChartCompanyId.Value)?.Name ?? string.Empty;

        Chart = await OrgChartBuilder.BuildAsync(_dbContext, ChartCompanyId.Value);
    }

    private async Task LoadPositionsAsync()
    {
        var counts = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.PositionId != null)
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

        var branchExists = await _dbContext.Branches
            .AnyAsync(x => x.Id == DepartmentInput.BranchId);

        if (!branchExists)
        {
            ErrorMessage = "الفرع المحدد غير موجود.";
            return RedirectToPage();
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

    private async Task LoadAsync()
    {
        TotalCompanies = await _dbContext.Companies.CountAsync();
        TotalBranches = await _dbContext.Branches.CountAsync();
        TotalDepartments = await _dbContext.Departments.CountAsync();
        TotalEmployees = await _dbContext.Employees.CountAsync();
        ActiveEmployees = await _dbContext.Employees.CountAsync(x => x.IsActive);
        InactiveEmployees = TotalEmployees - ActiveEmployees;

        var companyRows = await _dbContext.Companies
            .AsNoTracking()
            .Select(x => new CompanyViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code,
                IsActive = x.IsActive
            })
            .OrderBy(x => x.Name)
            .ToListAsync();

        var branchRows = await _dbContext.Branches
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
            .ToListAsync();

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

        var employeeCountsByBranch = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Department.BranchId.HasValue)
            .GroupBy(x => x.Department.BranchId!.Value)
            .Select(x => new
            {
                BranchId = x.Key,
                Count = x.Count()
            })
            .ToDictionaryAsync(x => x.BranchId, x => x.Count);

        var employeeCountsByDepartment = await _dbContext.Employees
            .AsNoTracking()
            .GroupBy(x => x.DepartmentId)
            .Select(x => new
            {
                DepartmentId = x.Key,
                Count = x.Count()
            })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

        foreach (var department in departmentRows)
        {
            department.EmployeeCount = employeeCountsByDepartment.TryGetValue(department.Id, out var employees)
                ? employees
                : 0;
        }

        foreach (var branch in branchRows)
        {
            branch.Departments = departmentRows
                .Where(x => x.BranchId == branch.Id)
                .ToList();

            branch.DepartmentCount = branch.Departments.Count;

            branch.EmployeeCount = employeeCountsByBranch.TryGetValue(branch.Id, out var employees)
                ? employees
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
