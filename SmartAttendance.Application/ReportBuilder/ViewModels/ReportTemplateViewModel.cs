namespace SmartAttendance.Application.ReportBuilder.ViewModels;

public class ReportTemplateViewModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ReportType { get; set; } = "Employees";

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? SearchTerm { get; set; }

    public string? BranchId { get; set; }

    public string? DepartmentId { get; set; }

    public string? ShiftId { get; set; }

    public string? StatusFilter { get; set; }

    public string? ActiveFilter { get; set; }

    // The order of this list is the report column order.
    public List<string> SelectedColumns { get; set; } = new();

    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
