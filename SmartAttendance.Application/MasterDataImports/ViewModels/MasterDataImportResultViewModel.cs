namespace SmartAttendance.Application.MasterDataImports.ViewModels;

public class MasterDataImportResultViewModel
{
    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ErrorCount { get; set; }

    public string Message { get; set; } = string.Empty;
}
