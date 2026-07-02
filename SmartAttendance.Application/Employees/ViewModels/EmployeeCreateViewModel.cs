namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeCreateViewModel
{
    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? NationalId { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? BirthDate { get; set; }

    public int DepartmentId { get; set; }
}
