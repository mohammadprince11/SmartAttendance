using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Pages.Organization;

public class ChartModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ChartModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName { get; set; } = string.Empty;

    /// <summary>Reporting roots that manage at least one employee (rendered as trees).</summary>
    public List<OrgNode> ManagerRoots { get; set; } = new();

    /// <summary>Roots with no manager and no reports — individual contributors not yet placed.</summary>
    public List<OrgNode> UnassignedRoots { get; set; } = new();

    public int TotalEmployees { get; set; }
    public int ManagerCount { get; set; }
    public int WithManagerCount { get; set; }
    public int WithoutManagerCount { get; set; }
    public int MaxDepth { get; set; }

    public async Task OnGetAsync()
    {
        CompanyOptions = await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new CompanyOption { Id = x.Id, Name = x.Name })
            .ToListAsync();

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

        await BuildChartAsync(CompanyId.Value);
    }

    private async Task BuildChartAsync(int companyId)
    {
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive
                     && !e.IsDeleted
                     && e.Branch.CompanyId == companyId)
            .Select(e => new OrgNode
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo,
                FullName = e.FullName,
                Position = e.Position ?? string.Empty,
                DepartmentName = e.Department.Name,
                PhotoPath = e.PhotoPath,
                ManagerId = e.DirectManagerId
            })
            .ToListAsync();

        TotalEmployees = employees.Count;

        if (TotalEmployees == 0)
        {
            return;
        }

        var byId = employees.ToDictionary(e => e.Id);

        // Link each node to its parent when the manager belongs to the same
        // company set. Managers outside the set are treated as "no manager"
        // so those employees surface as roots instead of disappearing.
        foreach (var node in employees)
        {
            if (node.ManagerId.HasValue &&
                byId.TryGetValue(node.ManagerId.Value, out var manager) &&
                manager.Id != node.Id)
            {
                manager.Children.Add(node);
                node.HasManagerInScope = true;
            }
        }

        WithManagerCount = employees.Count(e => e.HasManagerInScope);
        WithoutManagerCount = TotalEmployees - WithManagerCount;
        ManagerCount = employees.Count(e => e.Children.Count > 0);

        foreach (var node in employees.Where(e => e.Children.Count > 0))
        {
            node.Children.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        }

        var roots = employees
            .Where(e => !e.HasManagerInScope)
            .OrderByDescending(e => e.Children.Count)
            .ThenBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ManagerRoots = roots.Where(r => r.Children.Count > 0).ToList();
        UnassignedRoots = roots
            .Where(r => r.Children.Count == 0)
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MaxDepth = ManagerRoots.Count == 0
            ? (UnassignedRoots.Count > 0 ? 1 : 0)
            : ManagerRoots.Max(r => ComputeDepth(r, new HashSet<int>()));

        foreach (var root in ManagerRoots)
        {
            CountReports(root, new HashSet<int>());
        }
    }

    // Depth and report counting guard against accidental cycles in the data
    // (a manager chain that loops back) using a visited set per traversal.
    private static int ComputeDepth(OrgNode node, HashSet<int> visited)
    {
        if (!visited.Add(node.Id) || node.Children.Count == 0)
        {
            return 1;
        }

        var deepest = 0;
        foreach (var child in node.Children)
        {
            deepest = Math.Max(deepest, ComputeDepth(child, visited));
        }

        visited.Remove(node.Id);
        return deepest + 1;
    }

    private static int CountReports(OrgNode node, HashSet<int> visited)
    {
        if (!visited.Add(node.Id))
        {
            return 0;
        }

        var total = 0;
        foreach (var child in node.Children)
        {
            total += 1 + CountReports(child, visited);
        }

        node.TotalReports = total;
        return total;
    }

    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class OrgNode
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string? PhotoPath { get; set; }
        public int? ManagerId { get; set; }
        public bool HasManagerInScope { get; set; }
        public int TotalReports { get; set; }
        public List<OrgNode> Children { get; } = new();

        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FullName))
                {
                    return "؟";
                }

                var parts = FullName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                return parts.Length == 1
                    ? parts[0][..1]
                    : $"{parts[0][..1]}{parts[1][..1]}";
            }
        }
    }
}
