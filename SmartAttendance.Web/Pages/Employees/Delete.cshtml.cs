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
            ErrorMessage = "\u0644\u0645 \u064a\u062a\u0645 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0645\u0648\u0638\u0641.";
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

        TempData["SuccessMessage"] = "\u062a\u0645 \u0625\u064a\u0642\u0627\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0645\u0639 \u0627\u0644\u062d\u0641\u0627\u0638 \u0639\u0644\u0649 \u062c\u0645\u064a\u0639 \u0628\u064a\u0627\u0646\u0627\u062a\u0647 \u0648\u0633\u062c\u0644\u0627\u062a\u0647 \u0627\u0644\u062a\u0627\u0631\u064a\u062e\u064a\u0629.";

        return RedirectToPage("./Index");
    }
}

