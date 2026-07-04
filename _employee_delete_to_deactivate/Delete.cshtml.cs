using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class DeleteModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;

    public DeleteModel(IEmployeeService employeeService, ApplicationDbContext dbContext)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
    }

    public EmployeeDetailsViewModel Employee { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var employee = await _employeeService.GetByIdAsync(id);

        if (employee == null)
        {
            return NotFound();
        }

        Employee = employee;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var employee = await _employeeService.GetByIdAsync(id);

        if (employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف.";
            return Page();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
UPDATE Employees
SET IsActive = 0
WHERE Id = @Id;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
    VALUES
    (
        'Employee',
        CAST(@Id AS nvarchar(80)),
        'Employee Deactivated',
        @OldValues,
        @NewValues,
        @UserName,
        @IpAddress
    );
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@OldValues", "IsActive: True");
                HrmsDatabase.AddParameter(command, "@NewValues", "IsActive: False");
                HrmsDatabase.AddParameter(command, "@UserName", Request.Cookies["SA.UserName"] ?? "System");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        TempData["SuccessMessage"] = "تم إيقاف الموظف مع الحفاظ على جميع بياناته وسجلاته التاريخية.";

        return RedirectToPage("./Index");
    }
}
