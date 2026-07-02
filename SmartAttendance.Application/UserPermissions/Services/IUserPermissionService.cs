using SmartAttendance.Application.UserPermissions.ViewModels;

namespace SmartAttendance.Application.UserPermissions.Services;

public interface IUserPermissionService
{
    Task<IEnumerable<UserPermissionUserViewModel>> GetUsersAsync();

    Task<UserPermissionAssignmentViewModel?> GetAssignmentAsync(int systemUserId);

    Task<bool> SaveAssignmentsAsync(int systemUserId, List<int> selectedPermissionIds);
}
