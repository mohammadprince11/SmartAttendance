namespace SmartAttendance.Application.AttendanceImports.ViewModels;

public class AttendanceImportResultViewModel
{
    public int ImportedCount { get; set; }

    public int SkippedCount { get; set; }

    public int WarningImportedCount { get; set; }

    public int ErrorCount { get; set; }

    public string Message { get; set; } = string.Empty;
}
