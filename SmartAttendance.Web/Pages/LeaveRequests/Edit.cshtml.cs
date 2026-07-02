using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class EditModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;

    public EditModel(ILeaveRequestService leaveRequestService)
    {
        _leaveRequestService = leaveRequestService;
    }

    [BindProperty]
    public LeaveRequestEditViewModel LeaveRequest { get; set; } = new();

    public IEnumerable<EmployeeListViewModel> Employees { get; set; } = new List<EmployeeListViewModel>();

    public IEnumerable<SelectListItem> LeaveTypes { get; set; } = new List<SelectListItem>();

    public IEnumerable<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadDropdownsAsync();

        var leaveRequest = await _leaveRequestService.GetEditByIdAsync(id);

        if (leaveRequest == null)
            return NotFound();

        LeaveRequest = leaveRequest;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _leaveRequestService.UpdateAsync(LeaveRequest);

        if (!updated)
        {
            ErrorMessage = "Leave request not found, invalid employee, or invalid date range.";
            return Page();
        }

        TempData["SuccessMessage"] = "Leave request updated successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        Employees = await _leaveRequestService.GetEmployeesForDropdownAsync();

        LeaveTypes = Enum.GetValues<LeaveType>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();

        Statuses = Enum.GetValues<LeaveStatus>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
