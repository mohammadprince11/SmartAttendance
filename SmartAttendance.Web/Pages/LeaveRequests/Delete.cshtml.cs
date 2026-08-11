using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class DeleteModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(
        ILeaveRequestService leaveRequestService,
        ICompanyScopeProvider companyScope)
    {
        _leaveRequestService = leaveRequestService;
        _companyScope = companyScope;
    }

    public LeaveRequestDetailsViewModel LeaveRequest { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var scope = await BuildScopeAsync();
        var leaveRequest = await _leaveRequestService.GetByIdAsync(id, scope);

        // خارج النطاق يعود null كـ«غير موجود» تماماً — فلا يفرّق الردّ بين طلبٍ
        // لا وجود له وطلبٍ لشركةٍ أخرى (AUTHZ-003).
        if (leaveRequest == null)
            return NotFound();

        LeaveRequest = leaveRequest;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var scope = await BuildScopeAsync();
        var deleted = await _leaveRequestService.DeleteAsync(id, scope);

        if (!deleted)
        {
            ErrorMessage = "Leave request not found or could not be deleted.";

            var leaveRequest = await _leaveRequestService.GetByIdAsync(id, scope);
            if (leaveRequest != null)
                LeaveRequest = leaveRequest;

            return Page();
        }

        TempData["SuccessMessage"] = "Leave request deleted successfully.";

        return RedirectToPage("./Index");
    }

    private Task<LeaveRequestAccessScope> BuildScopeAsync() =>
        LeaveRequestScopeFactory.BuildAsync(_companyScope, User, HttpContext.RequestAborted);
}
