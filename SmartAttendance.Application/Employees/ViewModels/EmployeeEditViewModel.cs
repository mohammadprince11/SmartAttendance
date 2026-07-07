namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeEditViewModel
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

    
    
    
    
    public string? Country { get; set; }
public string? Nationality { get; set; }
public string? Gender { get; set; }
public string? MaritalStatus { get; set; }
public bool IsActive { get; set; } = true;

    public int DepartmentId { get; set; }
}
