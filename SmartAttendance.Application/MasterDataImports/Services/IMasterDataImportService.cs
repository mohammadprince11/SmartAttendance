using SmartAttendance.Application.MasterDataImports.ViewModels;

namespace SmartAttendance.Application.MasterDataImports.Services;

public interface IMasterDataImportService
{
    List<string> GetSupportedImportTypes();

    List<string> GetRequiredColumns(string importType);

    Task<MasterDataImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        string importType,
        int previewLimit = 500);

    Task<MasterDataImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName,
        string importType);
}
