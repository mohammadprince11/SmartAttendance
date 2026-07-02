using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceProcessing.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceProcessing;

public class IndexModel : PageModel
{
    private readonly IAttendanceProcessingService _attendanceProcessingService;

    public IndexModel(IAttendanceProcessingService attendanceProcessingService)
    {
        _attendanceProcessingService = attendanceProcessingService;
    }

    public IEnumerable<AttendanceProcessingResultViewModel> Records { get; set; } = new List<AttendanceProcessingResultViewModel>();

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        FromDate ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        ToDate ??= DateOnly.FromDateTime(DateTime.Today);

        Records = await _attendanceProcessingService.GetProcessedRecordsAsync(
            FromDate,
            ToDate,
            SearchTerm);
    }
}
