namespace SmartAttendance.Application.Departments.ViewModels;

public class DepartmentEditViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }

    public int BranchId { get; set; }
}
