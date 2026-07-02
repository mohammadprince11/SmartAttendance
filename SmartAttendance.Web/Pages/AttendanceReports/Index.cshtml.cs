using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceReports;

public class IndexModel : PageModel
{
    private readonly IAttendanceReportService _attendanceReportService;

    public IndexModel(IAttendanceReportService attendanceReportService)
    {
        _attendanceReportService = attendanceReportService;
    }

    public IEnumerable<MonthlyAttendanceReportViewModel> ReportRows { get; set; } = new List<MonthlyAttendanceReportViewModel>();

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public int TotalEmployees { get; set; }

    public int TotalPresentDays { get; set; }

    public int TotalAbsentDays { get; set; }

    public int TotalLeaveDays { get; set; }

    public int TotalHolidayDays { get; set; }

    public int TotalLateMinutes { get; set; }

    public decimal TotalWorkingHours { get; set; }

    public async Task OnGetAsync()
    {
        FromDate ??= new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate ??= DateOnly.FromDateTime(DateTime.Today);

        ReportRows = await _attendanceReportService.GetMonthlySummaryAsync(
            FromDate.Value,
            ToDate.Value,
            SearchTerm);

        TotalEmployees = ReportRows.Count();
        TotalPresentDays = ReportRows.Sum(x => x.PresentDays);
        TotalAbsentDays = ReportRows.Sum(x => x.AbsentDays);
        TotalLeaveDays = ReportRows.Sum(x => x.LeaveDays);
        TotalHolidayDays = ReportRows.Sum(x => x.HolidayDays);
        TotalLateMinutes = ReportRows.Sum(x => x.TotalLateMinutes);
        TotalWorkingHours = ReportRows.Sum(x => x.TotalWorkingHours);
    }
}
