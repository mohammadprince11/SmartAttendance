using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class EditModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;
    private readonly ApplicationDbContext _db;

    public EditModel(ILeaveRequestService leaveRequestService, ApplicationDbContext db)
    {
        _leaveRequestService = leaveRequestService;
        _db = db;
    }

    [BindProperty]
    public LeaveRequestEditViewModel LeaveRequest { get; set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — يبدأ بهما <c>_EmployeePicker</c> معبَّأً.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

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

        // ⚠️ بعد إسناد الطلب لا قبله: `LoadDropdownsAsync` يعمل قبل القراءة، فلو
        //    وُضع النداء هناك لبحث عن الموظف صفر وبدأ المنتقي فارغاً بالتعديل.
        await LoadPickerIdentityAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();
        await LoadPickerIdentityAsync();

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

    /// <summary>صفٌّ واحد للموظف المحسوم — المنتقي لا يحمّل قائمة.</summary>
    private async Task LoadPickerIdentityAsync()
    {
        var identity = await EmployeePickerLookup.LoadAsync(_db, LeaveRequest.EmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;
    }

    private async Task LoadDropdownsAsync()
    {
        LeaveTypes = Enum.GetValues<LeaveType>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();

        Statuses = Enum.GetValues<LeaveStatus>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
