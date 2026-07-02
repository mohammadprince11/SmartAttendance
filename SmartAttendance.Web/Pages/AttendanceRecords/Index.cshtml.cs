using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class IndexModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;

    public IndexModel(IAttendanceRecordService attendanceRecordService)
    {
        _attendanceRecordService = attendanceRecordService;
    }

    public IEnumerable<AttendanceRecordListViewModel> AttendanceRecords { get; set; } = new List<AttendanceRecordListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        AttendanceRecords = await _attendanceRecordService.GetAllAsync(SearchTerm);
    }
}
