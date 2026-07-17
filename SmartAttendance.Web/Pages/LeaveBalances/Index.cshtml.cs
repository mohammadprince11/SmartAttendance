using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.LeaveBalances;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName { get; set; } = string.Empty;

    public List<int> YearOptions { get; set; } = new();

    public IReadOnlyList<LeaveType> TrackedTypes => IraqiLeavePolicy.TrackedTypes;

    public List<EmployeeBalanceRow> Rows { get; set; } = new();

    public int EmployeeCount => Rows.Count;

    public int OverLimitCount => Rows.Count(r => r.Cells.Values.Any(c => c.Remaining < 0));

    public decimal TotalAnnualRemaining =>
        Rows.Sum(r => r.Cells.TryGetValue(LeaveType.Annual, out var c) ? c.Remaining : 0);

    public decimal TotalAnnualUsed =>
        Rows.Sum(r => r.Cells.TryGetValue(LeaveType.Annual, out var c) ? c.Used : 0);

    public async Task OnGetAsync()
    {
        await LeaveBalanceSchema.EnsureAsync(_dbContext);

        var currentYear = DateTime.Today.Year;
        YearOptions = Enumerable.Range(currentYear - 3, 5).OrderByDescending(y => y).ToList();
        if (Year < currentYear - 10 || Year > currentYear + 2)
        {
            Year = currentYear;
        }

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

        await BuildRowsAsync(CompanyId.Value);
    }

    private async Task BuildRowsAsync(int companyId)
    {
        var search = Search?.Trim();

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive
                     && !e.IsDeleted
                     && e.Branch.CompanyId == companyId
                     && (string.IsNullOrEmpty(search)
                         || e.FullName.Contains(search)
                         || e.EmployeeNo.Contains(search)))
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                e.Id,
                e.EmployeeNo,
                e.FullName,
                DepartmentName = e.Department.Name
            })
            .ToListAsync();

        if (employees.Count == 0)
        {
            return;
        }

        var employeeIds = employees.Select(e => e.Id).ToList();
        var trackedTypes = TrackedTypes.ToList();

        var yearStart = new DateOnly(Year, 1, 1);
        var yearEnd = new DateOnly(Year, 12, 31);

        // Stored grant/adjustment rows (overrides + carry-over) for this year.
        var overrides = await _dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.Year == Year && employeeIds.Contains(b.EmployeeId))
            .ToListAsync();

        var overrideLookup = overrides
            .GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(b => b.LeaveType, b => b));

        // Approved leave requests overlapping the selected year — consumption side.
        var requests = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.Status == LeaveStatus.Approved
                     && employeeIds.Contains(r.EmployeeId)
                     && trackedTypes.Contains(r.LeaveType)
                     && r.FromDate <= yearEnd
                     && r.ToDate >= yearStart)
            .Select(r => new { r.EmployeeId, r.LeaveType, r.FromDate, r.ToDate })
            .ToListAsync();

        var usedLookup = new Dictionary<(int EmployeeId, LeaveType Type), decimal>();
        foreach (var request in requests)
        {
            var start = request.FromDate > yearStart ? request.FromDate : yearStart;
            var end = request.ToDate < yearEnd ? request.ToDate : yearEnd;
            var days = end.DayNumber - start.DayNumber + 1;
            if (days <= 0)
            {
                continue;
            }

            var key = (request.EmployeeId, request.LeaveType);
            usedLookup[key] = usedLookup.GetValueOrDefault(key) + days;
        }

        foreach (var employee in employees)
        {
            var row = new EmployeeBalanceRow
            {
                EmployeeId = employee.Id,
                EmployeeNo = employee.EmployeeNo,
                FullName = employee.FullName,
                DepartmentName = employee.DepartmentName
            };

            overrideLookup.TryGetValue(employee.Id, out var employeeOverrides);

            foreach (var type in trackedTypes)
            {
                var hasOverride = employeeOverrides != null
                                  && employeeOverrides.TryGetValue(type, out var stored);
                var entitled = hasOverride
                    ? employeeOverrides![type].EntitledDays
                    : IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0;
                var carriedOver = hasOverride ? employeeOverrides![type].CarriedOverDays : 0;
                var used = usedLookup.GetValueOrDefault((employee.Id, type));

                row.Cells[type] = new BalanceCell
                {
                    Entitled = entitled,
                    CarriedOver = carriedOver,
                    Used = used,
                    HasOverride = hasOverride
                };
            }

            Rows.Add(row);
        }
    }

    public string LeaveTypeText(LeaveType type) => type switch
    {
        LeaveType.Annual => "سنوية",
        LeaveType.Sick => "مرضية",
        LeaveType.Unpaid => "بدون راتب",
        LeaveType.Emergency => "طارئة",
        LeaveType.Official => "رسمية",
        _ => type.ToString()
    };

    public string RemainingClass(decimal remaining)
    {
        if (remaining < 0)
        {
            return "rejected";
        }

        return remaining <= 3 ? "pending" : "approved";
    }

    public class CompanyOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class EmployeeBalanceRow
    {
        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public Dictionary<LeaveType, BalanceCell> Cells { get; set; } = new();
    }

    public class BalanceCell
    {
        public decimal Entitled { get; set; }

        public decimal CarriedOver { get; set; }

        public decimal Used { get; set; }

        public bool HasOverride { get; set; }

        public decimal Remaining => Entitled + CarriedOver - Used;
    }
}
