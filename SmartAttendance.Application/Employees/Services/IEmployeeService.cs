using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;

namespace SmartAttendance.Application.Employees.Services;

public interface IEmployeeService
{
    Task<EmployeePagedResultViewModel> GetPagedAsync(
        EmployeeListQueryViewModel query);

    Task<IEnumerable<EmployeeListViewModel>> GetAllAsync(
        string? searchTerm = null);

    Task<EmployeeDetailsViewModel?> GetByIdAsync(int id);

    Task<EmployeeEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(EmployeeCreateViewModel model);

    Task<bool> UpdateAsync(EmployeeEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> EmployeeNoExistsAsync(string employeeNo);

    Task<IEnumerable<DepartmentListViewModel>> GetDepartmentsForDropdownAsync(
        int? companyId = null);

    Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync(
        int? companyId = null);

    Task<IEnumerable<PositionOptionViewModel>> GetPositionsForDropdownAsync();
}
