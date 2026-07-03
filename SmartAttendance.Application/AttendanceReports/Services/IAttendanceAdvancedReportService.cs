using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Application.AttendanceReports.Services;

public interface IAttendanceAdvancedReportService
{
    Task<DailyAttendanceReportViewModel> GetDailyReportAsync(DateOnly fromDate, DateOnly toDate, string? searchTerm);

    Task<MonthlyAttendanceReportViewModel> GetMonthlyReportAsync(DateOnly fromDate, DateOnly toDate, string? searchTerm);
}
