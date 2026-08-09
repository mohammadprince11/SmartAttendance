namespace SmartAttendance.Application.AttendanceImports.Services;

/// <summary>Hard resource ceilings for untrusted attendance upload processing.</summary>
public static class AttendanceImportLimits
{
    public const long MaxCompressedBytes = 256L * 1024 * 1024;
    public const long MaxDecompressedBytes = 1024L * 1024 * 1024;
    public const long MaxZipEntryBytes = 512L * 1024 * 1024;
    public const int MaxCompressionRatio = 100;
    public const int MaxWorksheetRows = 1_000_000;
    public const int MaxEmployeeDayGroups = 500_000;
    public const int MaxSharedStrings = 250_000;
    public const int MaxReportedErrors = 200;
    public const int MaxProcessingSeconds = 600;
    public const int BulkCopyTimeoutSeconds = 120;
}
