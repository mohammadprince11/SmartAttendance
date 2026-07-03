using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceReports;

public class MonthlyModel : PageModel
{
    private readonly IAttendanceAdvancedReportService _reportService;

    public MonthlyModel(IAttendanceAdvancedReportService reportService)
    {
        _reportService = reportService;
    }

    public MonthlyAttendanceReportViewModel Report { get; set; } = new();

    public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));

    public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public string? SearchTerm { get; set; }

    public async Task OnGetAsync(DateOnly? fromDate, DateOnly? toDate, string? searchTerm)
    {
        FromDate = fromDate ?? DateOnly.FromDateTime(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        ToDate = toDate ?? DateOnly.FromDateTime(DateTime.Today);
        SearchTerm = searchTerm;

        Report = await _reportService.GetMonthlyReportAsync(FromDate, ToDate, SearchTerm);
    }
}
