namespace SmartAttendance.Application.MasterDataImports.ViewModels;

public class MasterDataImportPreviewRowViewModel
{
    public int RowNumber { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool CanImport { get; set; }

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
