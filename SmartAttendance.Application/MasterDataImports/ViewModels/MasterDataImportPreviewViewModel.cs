namespace SmartAttendance.Application.MasterDataImports.ViewModels;

public class MasterDataImportPreviewViewModel
{
    public string Token { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ImportType { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int ReadyCount { get; set; }

    public int ErrorCount { get; set; }

    public int CreateCount { get; set; }

    public int UpdateCount { get; set; }

    public int PreviewLimit { get; set; }

    public List<MasterDataImportPreviewRowViewModel> Rows { get; set; } = new();
}
