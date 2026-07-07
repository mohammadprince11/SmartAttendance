using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Dashboard;

public class EmployeesDistributionModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private static readonly IReadOnlyList<DistributionDefinition> Definitions = new List<DistributionDefinition>
    {
        new(
            "branches",
            "الفروع",
            "توزيع الموظفين حسب الفرع",
            """
SELECT ISNULL(NULLIF(b.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
GROUP BY ISNULL(NULLIF(b.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
        new(
            "departments",
            "الأقسام",
            "توزيع الموظفين حسب القسم",
            """
SELECT ISNULL(NULLIF(d.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
GROUP BY ISNULL(NULLIF(d.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
        new(
            "gender",
            "الجنس",
            "توزيع الموظفين حسب الجنس",
            "SELECT ISNULL(NULLIF(Gender, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Gender, ''), 'Not Set') ORDER BY Total DESC, Name"),
        new(
            "country",
            "البلد",
            "توزيع الموظفين حسب البلد",
            "SELECT ISNULL(NULLIF(Country, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Country, ''), 'Not Set') ORDER BY Total DESC, Name"),
        new(
            "nationality",
            "الجنسية",
            "توزيع الموظفين حسب الجنسية",
            "SELECT ISNULL(NULLIF(Nationality, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Nationality, ''), 'Not Set') ORDER BY Total DESC, Name"),
        new(
            "contractType",
            "نوع العقد",
            "توزيع الموظفين حسب نوع العقد",
            "SELECT ISNULL(NULLIF(ContractType, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(ContractType, ''), 'Not Set') ORDER BY Total DESC, Name")
    };

    public EmployeesDistributionModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "branches";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public string PageTitle { get; set; } = "توزيع الموظفين";
    public int TotalEmployees { get; set; }
    public int TotalCategoriesBeforeSearch { get; set; }
    public int DisplayedCategories => Rows.Count;
    public string TopName => Rows.FirstOrDefault()?.Name ?? "-";
    public int TopTotal => Rows.FirstOrDefault()?.Total ?? 0;
    public IReadOnlyList<DistributionDefinition> Tabs => Definitions;
    public List<DistributionRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var definition = ResolveDefinition(Type);
        Type = definition.Key;
        PageTitle = definition.Title;
        Search = Search?.Trim();

        TotalEmployees = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT COUNT(*) FROM Employees");

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            definition.Sql,
            null,
            reader => new DistributionRow
            {
                Name = NormalizeName(HrmsDatabase.GetString(reader, "Name")),
                Total = HrmsDatabase.GetInt(reader, "Total")
            });

        rows = rows
            .GroupBy(row => row.Name)
            .Select(group => new DistributionRow
            {
                Name = group.Key,
                Total = group.Sum(row => row.Total)
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Name)
            .ToList();

        TotalCategoriesBeforeSearch = rows.Count;

        if (!string.IsNullOrWhiteSpace(Search))
        {
            rows = rows
                .Where(row => row.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var percentBase = TotalEmployees > 0 ? TotalEmployees : rows.Sum(row => row.Total);

        foreach (var row in rows)
        {
            row.Percent = percentBase > 0
                ? Math.Round((decimal)row.Total * 100m / percentBase, 2)
                : 0m;
        }

        Rows = rows;
    }

    private static DistributionDefinition ResolveDefinition(string? key)
    {
        return Definitions.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? Definitions[0];
    }

    private static string NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) || name.Trim().Equals("Not Set", StringComparison.OrdinalIgnoreCase)
            ? "غير محدد"
            : name.Trim();
    }

    public record DistributionDefinition(string Key, string Label, string Title, string Sql);

    public class DistributionRow
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
        public decimal Percent { get; set; }
    }
}

