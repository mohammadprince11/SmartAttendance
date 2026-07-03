namespace SmartAttendance.Application.ReportBuilder.ViewModels;

public class ReportBuilderResultViewModel
{
    public string ReportType { get; set; } = string.Empty;

    public List<ReportBuilderColumnViewModel> Columns { get; set; } = new();

    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public Dictionary<string, string> Summary { get; set; } = new();

    public int TotalRows => Rows.Count;
}
