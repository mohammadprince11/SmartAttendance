namespace SmartAttendance.Application.Departments.ViewModels;

public class DepartmentListViewModel
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int BranchId { get; set; }

    public string BranchName { get; set; } = string.Empty;
}