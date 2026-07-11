namespace SmartAttendance.Application.Departments.ViewModels;

public class DepartmentCreateViewModel
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public int BranchId { get; set; }
}
