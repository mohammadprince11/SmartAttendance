using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Positions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<PositionSummaryRow> Positions { get; set; } = new();

    public int TotalPositions { get; set; }

    public int TotalEmployeesWithPositions { get; set; }

    public int ActiveEmployeesWithPositions { get; set; }

    public async Task OnGetAsync()
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Select(employee => new
            {
                employee.Position,
                employee.IsActive
            })
            .ToListAsync();

        var normalizedRows = employees
            .Select(employee => new
            {
                Position = NormalizePosition(employee.Position),
                employee.IsActive
            })
            .Where(employee => !string.IsNullOrWhiteSpace(employee.Position))
            .ToList();

        TotalEmployeesWithPositions = normalizedRows.Count;
        ActiveEmployeesWithPositions = normalizedRows.Count(employee => employee.IsActive);

        var query = normalizedRows
            .GroupBy(employee => employee.Position!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PositionSummaryRow
            {
                Name = group.Key,
                EmployeeCount = group.Count(),
                ActiveEmployeeCount = group.Count(employee => employee.IsActive),
                InactiveEmployeeCount = group.Count(employee => !employee.IsActive)
            });

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var searchText = Search.Trim();
            query = query.Where(position =>
                position.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        Positions = query
            .OrderBy(position => position.Name)
            .ToList();

        TotalPositions = Positions.Count;
    }

    private static string NormalizePosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }
}

public sealed class PositionSummaryRow
{
    public string Name { get; set; } = string.Empty;

    public int EmployeeCount { get; set; }

    public int ActiveEmployeeCount { get; set; }

    public int InactiveEmployeeCount { get; set; }
}