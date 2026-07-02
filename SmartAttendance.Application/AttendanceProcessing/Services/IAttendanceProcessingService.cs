using SmartAttendance.Application.AttendanceProcessing.ViewModels;

namespace SmartAttendance.Application.AttendanceProcessing.Services;

public interface IAttendanceProcessingService
{
    Task<IEnumerable<AttendanceProcessingResultViewModel>> GetProcessedRecordsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? searchTerm = null);
}
