using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.UserPermissions.Services;
using SmartAttendance.Application.UserPermissions.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class UserPermissionService : IUserPermissionService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserPermissionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<UserPermissionUserViewModel>> GetUsersAsync()
    {
        var users = await _unitOfWork.SystemUsers.GetAllAsync();

        return users
            .OrderBy(x => x.FullName)
            .Select(x => new UserPermissionUserViewModel
            {
                Id = x.Id,
                FullName = x.FullName,
                UserName = x.UserName,
                Role = x.Role.ToString(),
                IsActive = x.IsActive
            })
            .ToList();
    }

    public async Task<UserPermissionAssignmentViewModel?> GetAssignmentAsync(int systemUserId)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(systemUserId);

        if (user == null)
            return null;

        var permissions = await _unitOfWork.Permissions.GetAllAsync();
        var userPermissions = await _unitOfWork.SystemUserPermissions.GetAllAsync();

        var grantedPermissionIds = userPermissions
            .Where(x =>
                x.SystemUserId == systemUserId &&
                x.Effect == PermissionEffect.Allow &&
                x.ScopeType == PeopleDataScopeType.All &&
                !x.ValidFromUtc.HasValue &&
                !x.ValidToUtc.HasValue)
            .Select(x => x.PermissionId)
            .ToHashSet();

        var groups = permissions
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Module)
            .ThenBy(x => x.Code)
            .GroupBy(x => x.Module)
            .Select(group => new PermissionGroupViewModel
            {
                Module = group.Key,
                Permissions = group.Select(permission => new PermissionCheckViewModel
                {
                    PermissionId = permission.Id,
                    Code = permission.Code,
                    Name = permission.Name,
                    Description = permission.Description,
                    IsGranted = grantedPermissionIds.Contains(permission.Id)
                }).ToList()
            })
            .ToList();

        return new UserPermissionAssignmentViewModel
        {
            SystemUserId = user.Id,
            FullName = user.FullName,
            UserName = user.UserName,
            Role = user.Role.ToString(),
            Groups = groups
        };
    }

    public async Task<bool> SaveAssignmentsAsync(int systemUserId, List<int> selectedPermissionIds)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(systemUserId);

        if (user == null)
            return false;

        selectedPermissionIds = selectedPermissionIds
            .Distinct()
            .ToList();

        var allPermissions = await _unitOfWork.Permissions.GetAllAsync();
        var validPermissionIds = allPermissions.Select(x => x.Id).ToHashSet();

        selectedPermissionIds = selectedPermissionIds
            .Where(validPermissionIds.Contains)
            .ToList();

        var currentAssignments = (await _unitOfWork.SystemUserPermissions.GetAllAsync())
            .Where(x =>
                x.SystemUserId == systemUserId &&
                x.Effect == PermissionEffect.Allow &&
                x.ScopeType == PeopleDataScopeType.All &&
                !x.ValidFromUtc.HasValue &&
                !x.ValidToUtc.HasValue)
            .ToList();

        var selectedSet = selectedPermissionIds.ToHashSet();
        var currentSet = currentAssignments.Select(x => x.PermissionId).ToHashSet();

        var toDelete = currentAssignments
            .Where(x => !selectedSet.Contains(x.PermissionId))
            .ToList();

        foreach (var assignment in toDelete)
        {
            _unitOfWork.SystemUserPermissions.Delete(assignment);
        }

        var toAdd = selectedPermissionIds
            .Where(id => !currentSet.Contains(id))
            .ToList();

        foreach (var permissionId in toAdd)
        {
            await _unitOfWork.SystemUserPermissions.AddAsync(new SystemUserPermission
            {
                SystemUserId = systemUserId,
                PermissionId = permissionId,
                Effect = PermissionEffect.Allow,
                ScopeType = PeopleDataScopeType.All
            });
        }

        await _unitOfWork.SaveChangesAsync();

        return true;
    }
}
