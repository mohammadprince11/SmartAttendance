using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.ViewModels;

namespace SmartAttendance.Application.LeaveRequests.Services;

public interface ILeaveRequestService
{
    Task<IEnumerable<LeaveRequestListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<LeaveRequestDetailsViewModel?> GetByIdAsync(int id);

    Task<LeaveRequestEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(LeaveRequestCreateViewModel model);

    Task<bool> UpdateAsync(LeaveRequestEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync();
}
