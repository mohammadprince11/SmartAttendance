using SmartAttendance.Application.SystemUsers.ViewModels;

namespace SmartAttendance.Application.SystemUsers.Services;

public interface ISystemUserService
{
    Task<IEnumerable<SystemUserListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<SystemUserDetailsViewModel?> GetByIdAsync(int id);

    Task<SystemUserEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(SystemUserCreateViewModel model);

    Task<bool> UpdateAsync(SystemUserEditViewModel model);

    Task<bool> DeleteAsync(int id);
}
