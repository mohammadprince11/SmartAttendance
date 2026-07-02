namespace SmartAttendance.Application.Branches.ViewModels;

public class BranchEditViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }
}