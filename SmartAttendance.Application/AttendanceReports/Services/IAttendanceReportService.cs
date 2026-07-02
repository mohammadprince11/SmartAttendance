using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Application.AttendanceReports.Services;

public interface IAttendanceReportService
{
    Task<IEnumerable<MonthlyAttendanceReportViewModel>> GetMonthlySummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        string? searchTerm = null);
}
