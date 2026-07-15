using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.EmployeePermissions.Services;
using SmartAttendance.Application.EmployeePermissions.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class EmployeePermissionService : IEmployeePermissionService
{
    private readonly IUnitOfWork _unitOfWork;

    public EmployeePermissionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<EmployeePermissionEmployeeViewModel>> GetEmployeesAsync(string? searchTerm = null)
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var systemUsers = await _unitOfWork.SystemUsers.GetAllAsync();

        var linkedEmployeeIds = systemUsers
            .Where(x => x.EmployeeId.HasValue)
            .Select(x => x.EmployeeId!.Value)
            .ToHashSet();

        var result = employees
            .OrderBy(x => x.FullName)
            .Select(x => new EmployeePermissionEmployeeViewModel
            {
                EmployeeId = x.Id,
                EmployeeNo = x.EmployeeNo,
                FullName = x.FullName,
                Email = x.Email,
                IsActive = x.IsActive,
                HasSystemUser = linkedEmployeeIds.Contains(x.Id)
            });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Email != null && x.Email.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return result.ToList();
    }

    public async Task<EmployeePermissionAssignmentViewModel?> GetAssignmentAsync(int employeeId)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId);

        if (employee == null)
            return null;

        var systemUsers = await _unitOfWork.SystemUsers.GetAllAsync();

        var systemUser = systemUsers.FirstOrDefault(x => x.EmployeeId == employeeId);

        var permissions = await _unitOfWork.Permissions.GetAllAsync();
        var userPermissions = await _unitOfWork.SystemUserPermissions.GetAllAsync();

        var grantedPermissionIds = systemUser == null
            ? new HashSet<int>()
            : userPermissions
                .Where(x =>
                    x.SystemUserId == systemUser.Id &&
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
            .Select(group => new EmployeePermissionGroupViewModel
            {
                Module = group.Key,
                Permissions = group.Select(permission => new EmployeePermissionCheckViewModel
                {
                    PermissionId = permission.Id,
                    Code = permission.Code,
                    Name = permission.Name,
                    Description = permission.Description,
                    IsGranted = grantedPermissionIds.Contains(permission.Id)
                }).ToList()
            })
            .ToList();

        return new EmployeePermissionAssignmentViewModel
        {
            EmployeeId = employee.Id,
            SystemUserId = systemUser?.Id,
            EmployeeNo = employee.EmployeeNo,
            FullName = employee.FullName,
            Email = employee.Email,
            UserName = systemUser?.UserName ?? employee.EmployeeNo,
            HasSystemUser = systemUser != null,
            Groups = groups
        };
    }

    public async Task<bool> SaveAssignmentsAsync(int employeeId, List<int> selectedPermissionIds)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId);

        if (employee == null)
            return false;

        selectedPermissionIds = selectedPermissionIds
            .Distinct()
            .ToList();

        var allPermissions = await _unitOfWork.Permissions.GetAllAsync();
        var validPermissionIds = allPermissions.Select(x => x.Id).ToHashSet();

        selectedPermissionIds = selectedPermissionIds
            .Where(validPermissionIds.Contains)
            .ToList();

        var systemUsers = (await _unitOfWork.SystemUsers.GetAllAsync()).ToList();

        var systemUser = systemUsers.FirstOrDefault(x => x.EmployeeId == employeeId);

        if (systemUser == null)
        {
            systemUser = new SystemUser
            {
                EmployeeId = employee.Id,
                FullName = employee.FullName,
                UserName = BuildUniqueUserName(employee.EmployeeNo, employee.Id, systemUsers),
                Email = employee.Email,
                Role = SystemUserRole.Viewer,
                IsActive = employee.IsActive,
                Notes = "Created automatically from Employee Permissions page"
            };

            await _unitOfWork.SystemUsers.AddAsync(systemUser);
            await _unitOfWork.SaveChangesAsync();
        }
        else
        {
            systemUser.FullName = employee.FullName;
            systemUser.Email = employee.Email;
            systemUser.IsActive = employee.IsActive;

            if (string.IsNullOrWhiteSpace(systemUser.UserName))
                systemUser.UserName = BuildUniqueUserName(employee.EmployeeNo, employee.Id, systemUsers);

            _unitOfWork.SystemUsers.Update(systemUser);
            await _unitOfWork.SaveChangesAsync();
        }

        var currentAssignments = (await _unitOfWork.SystemUserPermissions.GetAllAsync())
            .Where(x =>
                x.SystemUserId == systemUser.Id &&
                x.Effect == PermissionEffect.Allow &&
                x.ScopeType == PeopleDataScopeType.All &&
                !x.ValidFromUtc.HasValue &&
                !x.ValidToUtc.HasValue)
            .ToList();

        var selectedSet = selectedPermissionIds.ToHashSet();
        var currentSet = currentAssignments.Select(x => x.PermissionId).ToHashSet();

        foreach (var assignment in currentAssignments.Where(x => !selectedSet.Contains(x.PermissionId)))
        {
            _unitOfWork.SystemUserPermissions.Delete(assignment);
        }

        foreach (var permissionId in selectedPermissionIds.Where(id => !currentSet.Contains(id)))
        {
            await _unitOfWork.SystemUserPermissions.AddAsync(new SystemUserPermission
            {
                SystemUserId = systemUser.Id,
                PermissionId = permissionId,
                Effect = PermissionEffect.Allow,
                ScopeType = PeopleDataScopeType.All
            });
        }

        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    private static string BuildUniqueUserName(string employeeNo, int employeeId, List<SystemUser> existingUsers)
    {
        var baseUserName = string.IsNullOrWhiteSpace(employeeNo)
            ? $"EMP{employeeId}"
            : employeeNo.Trim();

        var userName = baseUserName;

        var counter = 1;

        while (existingUsers.Any(x =>
            string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            userName = $"{baseUserName}_{counter}";
            counter++;
        }

        return userName;
    }
}
