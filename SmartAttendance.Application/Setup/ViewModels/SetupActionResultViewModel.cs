namespace SmartAttendance.Application.Setup.ViewModels;

public class SetupActionResultViewModel
{
    public bool Success { get; set; }

    public int AffectedCount { get; set; }

    public string Message { get; set; } = string.Empty;
}
