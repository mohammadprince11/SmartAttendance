namespace SmartAttendance.Application.ReportBuilder.ViewModels;

public class ReportBuilderRequestViewModel
{
    public string ReportType { get; set; } = "Employees";

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? SearchTerm { get; set; }

    public string? BranchId { get; set; }

    public string? DepartmentId { get; set; }

    public string? ShiftId { get; set; }

    public string? StatusFilter { get; set; }

    public string? ActiveFilter { get; set; }

    public List<string> SelectedColumns { get; set; } = new();
}
