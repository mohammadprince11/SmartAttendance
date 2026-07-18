using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Organization;

/// <summary>
/// A single node in the managerial reporting tree (built from DirectManagerId).
/// Top-level type so it can be shared by the standalone chart page and the
/// merged "hierarchical" tab on the Organization hub, and by the _OrgNode partial.
/// </summary>
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

            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length == 1
                ? parts[0][..1]
                : $"{parts[0][..1]}{parts[1][..1]}";
        }
    }
}

/// <summary>Result of building the managerial reporting tree for one company.</summary>
public class OrgChartData
{
    public List<OrgNode> ManagerRoots { get; set; } = new();
    public List<OrgNode> UnassignedRoots { get; set; } = new();
    public int TotalEmployees { get; set; }
    public int ManagerCount { get; set; }
    public int WithManagerCount { get; set; }
    public int WithoutManagerCount { get; set; }
    public int MaxDepth { get; set; }
}

/// <summary>
/// Builds the managerial reporting tree from employees' DirectManagerId within a
/// single company. Shared by ChartModel and the Organization hub's hierarchical tab.
/// </summary>
public static class OrgChartBuilder
{
    public static async Task<OrgChartData> BuildAsync(ApplicationDbContext dbContext, int companyId)
    {
        var result = new OrgChartData();

        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.Branch.CompanyId == companyId)
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

        result.TotalEmployees = employees.Count;

        if (employees.Count == 0)
        {
            return result;
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

        result.WithManagerCount = employees.Count(e => e.HasManagerInScope);
        result.WithoutManagerCount = result.TotalEmployees - result.WithManagerCount;
        result.ManagerCount = employees.Count(e => e.Children.Count > 0);

        foreach (var node in employees.Where(e => e.Children.Count > 0))
        {
            node.Children.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        }

        var roots = employees
            .Where(e => !e.HasManagerInScope)
            .OrderByDescending(e => e.Children.Count)
            .ThenBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.ManagerRoots = roots.Where(r => r.Children.Count > 0).ToList();
        result.UnassignedRoots = roots
            .Where(r => r.Children.Count == 0)
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.MaxDepth = result.ManagerRoots.Count == 0
            ? (result.UnassignedRoots.Count > 0 ? 1 : 0)
            : result.ManagerRoots.Max(r => ComputeDepth(r, new HashSet<int>()));

        foreach (var root in result.ManagerRoots)
        {
            CountReports(root, new HashSet<int>());
        }

        return result;
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
}
