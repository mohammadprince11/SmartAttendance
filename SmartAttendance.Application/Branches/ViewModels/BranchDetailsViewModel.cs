namespace SmartAttendance.Application.Branches.ViewModels;

public class BranchDetailsViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public bool IsActive { get; set; }

    public int CompanyId { get; set; }

    public string CompanyName { get; set; } = string.Empty;
}