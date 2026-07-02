using SmartAttendance.Application.EmployeePermissions.ViewModels;

namespace SmartAttendance.Application.EmployeePermissions.Services;

public interface IEmployeePermissionService
{
    Task<IEnumerable<EmployeePermissionEmployeeViewModel>> GetEmployeesAsync(string? searchTerm = null);

    Task<EmployeePermissionAssignmentViewModel?> GetAssignmentAsync(int employeeId);

    Task<bool> SaveAssignmentsAsync(int employeeId, List<int> selectedPermissionIds);
}
