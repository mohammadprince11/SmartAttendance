namespace SmartAttendance.Application.Employees.ViewModels;

public class PositionOptionViewModel
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
