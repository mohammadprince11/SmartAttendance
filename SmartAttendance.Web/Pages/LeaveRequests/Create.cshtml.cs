using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class CreateModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;
    private readonly ApplicationDbContext _db;

    public CreateModel(ILeaveRequestService leaveRequestService, ApplicationDbContext db)
    {
        _leaveRequestService = leaveRequestService;
        _db = db;
    }

    [BindProperty]
    public LeaveRequestCreateViewModel LeaveRequest { get; set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — يبدأ بهما <c>_EmployeePicker</c> معبَّأً.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

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
        // المنتقي لا يحمّل أحداً — صفٌّ واحد للموظف المحسوم إن وُجد.
        var identity = await EmployeePickerLookup.LoadAsync(_db, LeaveRequest.EmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;

        LeaveTypes = Enum.GetValues<LeaveType>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
