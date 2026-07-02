using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.EmployeeShifts;

public class CreateModel : PageModel
{
    private readonly IEmployeeShiftService _employeeShiftService;

    public CreateModel(IEmployeeShiftService employeeShiftService)
    {
        _employeeShiftService = employeeShiftService;
    }

    [BindProperty]
    public EmployeeShiftCreateViewModel EmployeeShift { get; set; } = new();

    public IEnumerable<EmployeeListViewModel> Employees { get; set; } = new List<EmployeeListViewModel>();

    public IEnumerable<ShiftListViewModel> Shifts { get; set; } = new List<ShiftListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDropdownsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();

        if (!ModelState.IsValid)
            return Page();

        var created = await _employeeShiftService.CreateAsync(EmployeeShift);

        if (!created)
        {
            ErrorMessage = "Invalid employee, shift, or effective dates.";
            return Page();
        }

        TempData["SuccessMessage"] = "Employee shift assigned successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        Employees = await _employeeShiftService.GetEmployeesForDropdownAsync();
        Shifts = await _employeeShiftService.GetShiftsForDropdownAsync();
    }
}
