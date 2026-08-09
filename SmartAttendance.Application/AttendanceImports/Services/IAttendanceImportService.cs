using SmartAttendance.Application.AttendanceImports.ViewModels;

namespace SmartAttendance.Application.AttendanceImports.Services;

public interface IAttendanceImportService
{
    Task<AttendanceImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        AttendanceImportScope scope,
        int previewLimit = 500,
        CancellationToken cancellationToken = default);

    Task<AttendanceImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName,
        AttendanceImportScope scope,
        CancellationToken cancellationToken = default);
}
