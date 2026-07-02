using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.EmployeeShifts;

public class EditModel : PageModel
{
    private readonly IEmployeeShiftService _employeeShiftService;

    public EditModel(IEmployeeShiftService employeeShiftService)
    {
        _employeeShiftService = employeeShiftService;
    }

    [BindProperty]
    public EmployeeShiftEditViewModel EmployeeShift { get; set; } = new();

    public IEnumerable<EmployeeListViewModel> Employees { get; set; } = new List<EmployeeListViewModel>();

    public IEnumerable<ShiftListViewModel> Shifts { get; set; } = new List<ShiftListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadDropdownsAsync();

        var employeeShift = await _employeeShiftService.GetEditByIdAsync(id);

        if (employeeShift == null)
            return NotFound();

        EmployeeShift = employeeShift;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _employeeShiftService.UpdateAsync(EmployeeShift);

        if (!updated)
        {
            ErrorMessage = "Employee shift not found, invalid employee, invalid shift, or invalid dates.";
            return Page();
        }

        TempData["SuccessMessage"] = "Employee shift updated successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        Employees = await _employeeShiftService.GetEmployeesForDropdownAsync();
        Shifts = await _employeeShiftService.GetShiftsForDropdownAsync();
    }
}
