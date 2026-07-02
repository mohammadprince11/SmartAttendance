using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class DeleteModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;

    public DeleteModel(ILeaveRequestService leaveRequestService)
    {
        _leaveRequestService = leaveRequestService;
    }

    public LeaveRequestDetailsViewModel LeaveRequest { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var leaveRequest = await _leaveRequestService.GetByIdAsync(id);

        if (leaveRequest == null)
            return NotFound();

        LeaveRequest = leaveRequest;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _leaveRequestService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Leave request not found or could not be deleted.";

            var leaveRequest = await _leaveRequestService.GetByIdAsync(id);
            if (leaveRequest != null)
                LeaveRequest = leaveRequest;

            return Page();
        }

        TempData["SuccessMessage"] = "Leave request deleted successfully.";

        return RedirectToPage("./Index");
    }
}
