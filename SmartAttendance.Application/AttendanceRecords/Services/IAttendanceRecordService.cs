using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;

namespace SmartAttendance.Application.AttendanceRecords.Services;

public interface IAttendanceRecordService
{
    Task<IEnumerable<AttendanceRecordListViewModel>> GetAllAsync(string? searchTerm = null, int maxResults = 50);

    Task<int> CountAsync(string? searchTerm = null);

    Task<AttendanceRecordDetailsViewModel?> GetByIdAsync(int id);

    Task<AttendanceRecordEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(AttendanceRecordCreateViewModel model);

    Task<bool> UpdateAsync(AttendanceRecordEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync();

    Task<IEnumerable<DeviceListViewModel>> GetDevicesForDropdownAsync();
}
