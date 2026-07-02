using SmartAttendance.Application.Permissions.ViewModels;

namespace SmartAttendance.Application.Permissions.Services;

public interface IPermissionService
{
    Task<IEnumerable<PermissionListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<PermissionDetailsViewModel?> GetByIdAsync(int id);

    Task<PermissionEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(PermissionCreateViewModel model);

    Task<bool> UpdateAsync(PermissionEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<int> SeedDefaultPermissionsAsync();
}
