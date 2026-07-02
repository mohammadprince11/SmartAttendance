using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.AttendanceReports.ViewModels;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceReportService : IAttendanceReportService
{
    private readonly IAttendanceProcessingService _attendanceProcessingService;

    public AttendanceReportService(IAttendanceProcessingService attendanceProcessingService)
    {
        _attendanceProcessingService = attendanceProcessingService;
    }

    public async Task<IEnumerable<MonthlyAttendanceReportViewModel>> GetMonthlySummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        string? searchTerm = null)
    {
        if (toDate < fromDate)
            return new List<MonthlyAttendanceReportViewModel>();

        var processedRecords = await _attendanceProcessingService.GetProcessedRecordsAsync(
            fromDate,
            toDate,
            searchTerm);

        var summary = processedRecords
            .GroupBy(x => new
            {
                x.EmployeeId,
                x.EmployeeNo,
                x.EmployeeName
            })
            .Select(group => new MonthlyAttendanceReportViewModel
            {
                EmployeeId = group.Key.EmployeeId,
                EmployeeNo = group.Key.EmployeeNo,
                EmployeeName = group.Key.EmployeeName,

                TotalDays = group.Select(x => x.AttendanceDate).Distinct().Count(),

                PresentDays = group.Count(x => IsStatus(x.CalculatedStatus, "Present")),

                LateDays = group.Count(x =>
                    IsStatus(x.CalculatedStatus, "Late") ||
                    x.LateMinutes > 0),

                AbsentDays = group.Count(x => IsStatus(x.CalculatedStatus, "Absent")),

                LeaveDays = group.Count(x => IsStatus(x.CalculatedStatus, "Leave")),

                HolidayDays = group.Count(x => IsStatus(x.CalculatedStatus, "Holiday")),

                EarlyLeaveDays = group.Count(x =>
                    IsStatus(x.CalculatedStatus, "Early Leave") ||
                    (x.EarlyLeaveMinutes.HasValue && x.EarlyLeaveMinutes.Value > 0)),

                MissingCheckOutDays = group.Count(x => IsStatus(x.CalculatedStatus, "Missing Check Out")),

                TotalLateMinutes = group.Sum(x => x.LateMinutes),

                TotalEarlyLeaveMinutes = group.Sum(x => x.EarlyLeaveMinutes ?? 0),

                TotalWorkingHours = Math.Round(group.Sum(x => x.WorkingHours ?? 0), 2)
            })
            .OrderBy(x => x.EmployeeName)
            .ToList();

        return summary;
    }

    private static bool IsStatus(string? value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
