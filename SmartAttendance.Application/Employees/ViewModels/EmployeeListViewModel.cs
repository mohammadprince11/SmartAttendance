namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeListViewModel
{
    public int Id { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? NationalId { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Position { get; set; }

    public DateOnly HireDate { get; set; }

    public DateOnly? BirthDate { get; set; }

    public bool IsActive { get; set; }

    public int DepartmentId { get; set; }

    public string DepartmentCode { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public string BranchName { get; set; } = string.Empty;
}
