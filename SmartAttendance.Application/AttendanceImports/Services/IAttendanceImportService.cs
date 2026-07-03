using SmartAttendance.Application.AttendanceImports.ViewModels;

namespace SmartAttendance.Application.AttendanceImports.Services;

public interface IAttendanceImportService
{
    Task<AttendanceImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        int previewLimit = 500);

    Task<AttendanceImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName);
}
