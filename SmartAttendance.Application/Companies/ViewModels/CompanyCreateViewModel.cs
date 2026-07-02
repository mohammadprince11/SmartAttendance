namespace SmartAttendance.Application.Companies.ViewModels;

public class CompanyCreateViewModel
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }
}