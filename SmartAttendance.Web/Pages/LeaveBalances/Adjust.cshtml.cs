using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.LeaveBalances;

public class AdjustModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public AdjustModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int EmployeeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    [BindProperty]
    public List<TypeInput> Inputs { get; set; } = new();

    public IReadOnlyList<LeaveType> TrackedTypes => IraqiLeavePolicy.TrackedTypes;

    public async Task<IActionResult> OnGetAsync()
    {
        await LeaveBalanceSchema.EnsureAsync(_dbContext);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == EmployeeId && !e.IsDeleted)
            .Select(e => new { e.EmployeeNo, e.FullName })
            .FirstOrDefaultAsync();

        if (employee == null)
        {
            return NotFound();
        }

        EmployeeNo = employee.EmployeeNo;
        FullName = employee.FullName;

        var overrides = await _dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.EmployeeId == EmployeeId && b.Year == Year)
            .ToListAsync();

        Inputs = TrackedTypes.Select(type =>
        {
            var stored = overrides.FirstOrDefault(b => b.LeaveType == type);
            return new TypeInput
            {
                LeaveType = type,
                EntitledDays = stored?.EntitledDays
                               ?? IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0,
                CarriedOverDays = stored?.CarriedOverDays ?? 0,
                Note = stored?.Note
            };
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LeaveBalanceSchema.EnsureAsync(_dbContext);

        var employeeExists = await _dbContext.Employees
            .AnyAsync(e => e.Id == EmployeeId && !e.IsDeleted);

        if (!employeeExists)
        {
            return NotFound();
        }

        var trackedTypes = TrackedTypes.ToHashSet();
        var userName = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;

        var existing = await _dbContext.LeaveBalances
            .Where(b => b.EmployeeId == EmployeeId && b.Year == Year)
            .ToListAsync();

        foreach (var input in Inputs)
        {
            if (!trackedTypes.Contains(input.LeaveType))
            {
                continue;
            }

            var entitled = Math.Clamp(input.EntitledDays, 0m, 366m);
            var carried = Math.Clamp(input.CarriedOverDays, 0m, 366m);
            var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            var row = existing.FirstOrDefault(b => b.LeaveType == input.LeaveType);
            if (row == null)
            {
                _dbContext.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = EmployeeId,
                    Year = Year,
                    LeaveType = input.LeaveType,
                    EntitledDays = entitled,
                    CarriedOverDays = carried,
                    Note = note,
                    CreatedAt = now,
                    CreatedBy = userName
                });
            }
            else
            {
                row.EntitledDays = entitled;
                row.CarriedOverDays = carried;
                row.Note = note;
                row.UpdatedAt = now;
                row.UpdatedBy = userName;
            }
        }

        await _dbContext.SaveChangesAsync();

        return RedirectToPage("Index", new { CompanyId, Year });
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

    public class TypeInput
    {
        public LeaveType LeaveType { get; set; }

        public decimal EntitledDays { get; set; }

        public decimal CarriedOverDays { get; set; }

        public string? Note { get; set; }
    }
}
