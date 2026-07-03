using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceReports;

public class DailyModel : PageModel
{
    private readonly IAttendanceAdvancedReportService _reportService;

    public DailyModel(IAttendanceAdvancedReportService reportService)
    {
        _reportService = reportService;
    }

    public DailyAttendanceReportViewModel Report { get; set; } = new();

    public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public string? SearchTerm { get; set; }

    public async Task OnGetAsync(DateOnly? fromDate, DateOnly? toDate, string? searchTerm)
    {
        FromDate = fromDate ?? DateOnly.FromDateTime(DateTime.Today);
        ToDate = toDate ?? FromDate;
        SearchTerm = searchTerm;

        Report = await _reportService.GetDailyReportAsync(FromDate, ToDate, SearchTerm);
    }
}
