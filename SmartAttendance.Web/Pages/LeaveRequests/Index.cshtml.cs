using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class IndexModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;

    public IndexModel(ILeaveRequestService leaveRequestService)
    {
        _leaveRequestService = leaveRequestService;
    }

    public IEnumerable<LeaveRequestListViewModel> LeaveRequests { get; set; } = new List<LeaveRequestListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        LeaveRequests = await _leaveRequestService.GetAllAsync(SearchTerm);
    }
}
