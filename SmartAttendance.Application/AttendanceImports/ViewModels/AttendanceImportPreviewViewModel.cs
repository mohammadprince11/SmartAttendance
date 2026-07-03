namespace SmartAttendance.Application.AttendanceImports.ViewModels;

public class AttendanceImportPreviewViewModel
{
    public string Token { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public int TotalRawRows { get; set; }

    public int TotalGroups { get; set; }

    public int ReadyToImportCount { get; set; }

    public int WarningCount { get; set; }

    public int ErrorCount { get; set; }

    public int ExistingRecordsCount { get; set; }

    public int PreviewLimit { get; set; }

    public List<AttendanceImportRowViewModel> Rows { get; set; } = new();
}
