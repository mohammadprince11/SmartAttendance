using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.LeaveRequests;

public class IndexModel : PageModel
{
    private readonly ILeaveRequestService _leaveRequestService;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(
        ILeaveRequestService leaveRequestService,
        ICompanyScopeProvider companyScope)
    {
        _leaveRequestService = leaveRequestService;
        _companyScope = companyScope;
    }

    public IEnumerable<LeaveRequestListViewModel> LeaveRequests { get; set; } = new List<LeaveRequestListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await LeaveRequestScopeFactory.BuildAsync(
            _companyScope, User, HttpContext.RequestAborted);

        LeaveRequests = await _leaveRequestService.GetAllAsync(scope, SearchTerm);
    }
}
