using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Devices.ViewModels;

namespace SmartAttendance.Application.Devices.Services;

public interface IDeviceService
{
    Task<IEnumerable<DeviceListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<DeviceDetailsViewModel?> GetByIdAsync(int id);

    Task<DeviceEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(DeviceCreateViewModel model);

    Task<bool> UpdateAsync(DeviceEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> SerialNumberExistsAsync(string serialNumber);

    Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync();
}
