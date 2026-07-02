using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class CreateModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;

    public CreateModel(ILeaveRequestService leaveRequestService)
    {
        _leaveRequestService = leaveRequestService;
    }

    [BindProperty]
    public LeaveRequestCreateViewModel LeaveRequest { get; set; } = new();

    public IEnumerable<EmployeeListViewModel> Employees { get; set; } = new List<EmployeeListViewModel>();

    public IEnumerable<SelectListItem> LeaveTypes { get; set; } = new List<SelectListItem>();

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

        var created = await _leaveRequestService.CreateAsync(LeaveRequest);

        if (!created)
        {
            ErrorMessage = "Invalid employee or leave date range.";
            return Page();
        }

        TempData["SuccessMessage"] = "Approved leave registered successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        Employees = await _leaveRequestService.GetEmployeesForDropdownAsync();

        LeaveTypes = Enum.GetValues<LeaveType>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
