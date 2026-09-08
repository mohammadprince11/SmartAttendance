using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Organization;

public class ChartModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public ChartModel(
        ApplicationDbContext dbContext,
        ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName { get; set; } = string.Empty;

    public List<OrgNode> ManagerRoots { get; set; } = new();
    public List<OrgNode> UnassignedRoots { get; set; } = new();

    public int TotalEmployees { get; set; }
    public int ManagerCount { get; set; }
    public int WithManagerCount { get; set; }
    public int WithoutManagerCount { get; set; }
    public int MaxDepth { get; set; }

    public async Task OnGetAsync()
    {
        // النطاق يُطبَّق قبل بناء أي مُنتقٍ: القائمة المعروضة هي المسموحة فقط، فلا
        // يستطيع دورٌ مقيَّد اختيار شركة أخرى — و`Resolve` يرفض أيضاً أي CompanyId
        // مُمرَّر بالرابط ليس ضمنها فيسقط لأول شركةٍ مسموحة لا لهيكل شركةٍ أجنبية.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        CompanyOptions = (await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new CompanyOption { Id = x.Id, Name = x.Name })
            .ToListAsync())
            .Where(x => scope.Allows(x.Id))
            .ToList();

        var localizedCompanyNames =
            await EmployeeBusinessDataDisplayLocalizer
                .GetCompanyNamesAsync(
                    _dbContext,
                    CompanyOptions.Select(item =>
                        (item.Id, item.Name)),
                    HttpContext.RequestAborted);

        foreach (var company in CompanyOptions)
        {
            if (localizedCompanyNames.TryGetValue(
                    company.Id,
                    out var localizedName))
            {
                company.Name = localizedName;
            }
        }

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        if (!CompanyId.HasValue)
        {
            return;
        }

        SelectedCompanyName = CompanyOptions
            .FirstOrDefault(x => x.Id == CompanyId.Value)?.Name ?? string.Empty;

        var data = await OrgChartBuilder.BuildAsync(_dbContext, CompanyId.Value);

        ManagerRoots = data.ManagerRoots;
        UnassignedRoots = data.UnassignedRoots;
        TotalEmployees = data.TotalEmployees;
        ManagerCount = data.ManagerCount;
        WithManagerCount = data.WithManagerCount;
        WithoutManagerCount = data.WithoutManagerCount;
        MaxDepth = data.MaxDepth;
    }

    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
