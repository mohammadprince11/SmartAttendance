using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.EmployeeShifts.ViewModels;

namespace SmartAttendance.Web.Pages.EmployeeShifts;

public class DeleteModel : PageModel
{
    private readonly IEmployeeShiftService _employeeShiftService;

    public DeleteModel(IEmployeeShiftService employeeShiftService)
    {
        _employeeShiftService = employeeShiftService;
    }

    public EmployeeShiftDetailsViewModel EmployeeShift { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var employeeShift = await _employeeShiftService.GetByIdAsync(id);

        if (employeeShift == null)
            return NotFound();

        EmployeeShift = employeeShift;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _employeeShiftService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Employee shift not found or could not be deleted.";

            var employeeShift = await _employeeShiftService.GetByIdAsync(id);
            if (employeeShift != null)
                EmployeeShift = employeeShift;

            return Page();
        }

        TempData["SuccessMessage"] = "Employee shift deleted successfully.";

        return RedirectToPage("./Index");
    }
}
