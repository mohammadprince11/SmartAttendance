using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Dashboard;

public class EmployeesDistributionModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<EmployeesDistributionModel> _logger;

    private static readonly IReadOnlyList<DistributionDefinition>
        Definitions = new List<DistributionDefinition>
        {
            new(
                "branches",
                "الفروع",
                "توزيع الموظفين حسب الفرع",
                """
SELECT ISNULL(NULLIF(b.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
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
INNER JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Departments d
    ON e.DepartmentId = d.Id
   AND d.IsDeleted = 0
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(d.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
            new(
                "gender",
                "الجنس",
                "توزيع الموظفين حسب الجنس",
                """
SELECT ISNULL(NULLIF(e.Gender, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Gender, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
            new(
                "country",
                "البلد",
                "توزيع الموظفين حسب البلد",
                """
SELECT ISNULL(NULLIF(e.Country, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Country, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
            new(
                "nationality",
                "الجنسية",
                "توزيع الموظفين حسب الجنسية",
                """
SELECT ISNULL(NULLIF(e.Nationality, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Nationality, ''), 'Not Set')
ORDER BY Total DESC, Name;
"""),
            new(
                "contractType",
                "نوع العقد",
                "توزيع الموظفين حسب نوع العقد",
                """
SELECT ISNULL(NULLIF(e.ContractType, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.ContractType, ''), 'Not Set')
ORDER BY Total DESC, Name;
""")
        };

    public EmployeesDistributionModel(
        ApplicationDbContext dbContext,
        ILogger<EmployeesDistributionModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "branches";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName =>
        CompanyId.HasValue
            ? CompanyOptions.FirstOrDefault(x =>
                  x.Id == CompanyId.Value)?.Name ?? "-"
            : "-";

    public string PageTitle { get; set; } = "توزيع الموظفين";
    public int TotalEmployees { get; set; }
    public int TotalCategoriesBeforeSearch { get; set; }
    public int DisplayedCategories => Rows.Count;
    public string TopName => Rows.FirstOrDefault()?.Name ?? "-";
    public int TopTotal => Rows.FirstOrDefault()?.Total ?? 0;

    public IReadOnlyList<DistributionDefinition> Tabs =>
        Definitions;

    public List<DistributionRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        CompanyOptions = await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new CompanyOption
            {
                Id = x.Id,
                Name = x.Name
            })
            .ToListAsync();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        var definition = ResolveDefinition(Type);
        Type = definition.Key;
        PageTitle = definition.Title;
        Search = Search?.Trim();

        if (!CompanyId.HasValue)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Employee distribution loaded without a company in {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
            return;
        }

        await LoadDistributionAsync(definition.Sql);

        stopwatch.Stop();
        _logger.LogInformation(
            "Employee distribution {DistributionType} loaded for company {CompanyId} in {ElapsedMilliseconds} ms using one SQL batch.",
            Type,
            CompanyId.Value,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task LoadDistributionAsync(string distributionSql)
    {
        var sql = $"""
SET NOCOUNT ON;

SELECT COUNT(*) AS TotalEmployees
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId;

{distributionSql}
""";

        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _dbContext.Database
                .CurrentTransaction?
                .GetDbTransaction();

            HrmsDatabase.AddParameter(
                command,
                "@CompanyId",
                CompanyId!.Value);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                TotalEmployees = HrmsDatabase.GetInt(
                    reader,
                    "TotalEmployees");
            }

            await reader.NextResultAsync();

            var rows = new List<DistributionRow>();

            while (await reader.ReadAsync())
            {
                rows.Add(new DistributionRow
                {
                    Name = NormalizeName(
                        HrmsDatabase.GetString(reader, "Name")),
                    Total = HrmsDatabase.GetInt(reader, "Total")
                });
            }

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
                    .Where(row => row.Name.Contains(
                        Search,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var percentBase = TotalEmployees > 0
                ? TotalEmployees
                : rows.Sum(row => row.Total);

            foreach (var row in rows)
            {
                row.Percent = percentBase > 0
                    ? Math.Round(
                        (decimal)row.Total * 100m / percentBase,
                        2)
                    : 0m;
            }

            Rows = rows;
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static DistributionDefinition ResolveDefinition(
        string? key)
    {
        return Definitions.FirstOrDefault(item =>
                   item.Key.Equals(
                       key,
                       StringComparison.OrdinalIgnoreCase))
               ?? Definitions[0];
    }

    private static string NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ||
               name.Trim().Equals(
                   "Not Set",
                   StringComparison.OrdinalIgnoreCase)
            ? "غير محدد"
            : name.Trim();
    }

    public record DistributionDefinition(
        string Key,
        string Label,
        string Title,
        string Sql);

    public class CompanyOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class DistributionRow
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
        public decimal Percent { get; set; }
    }
}
