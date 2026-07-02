namespace SmartAttendance.Application.Branches.ViewModels;

public class BranchCreateViewModel
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public int CompanyId { get; set; }
}