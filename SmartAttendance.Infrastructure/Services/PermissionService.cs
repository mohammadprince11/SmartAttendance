using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Permissions.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class PermissionService : IPermissionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public PermissionService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<PermissionListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync();

        var result = _mapper.Map<IEnumerable<PermissionListViewModel>>(permissions);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Module.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Description != null && x.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Module)
            .ThenBy(x => x.Code)
            .ToList();
    }

    public async Task<PermissionDetailsViewModel?> GetByIdAsync(int id)
    {
        var permission = await _unitOfWork.Permissions.GetByIdAsync(id);

        if (permission == null)
            return null;

        return _mapper.Map<PermissionDetailsViewModel>(permission);
    }

    public async Task<PermissionEditViewModel?> GetEditByIdAsync(int id)
    {
        var permission = await _unitOfWork.Permissions.GetByIdAsync(id);

        if (permission == null)
            return null;

        return _mapper.Map<PermissionEditViewModel>(permission);
    }

    public async Task<bool> CreateAsync(PermissionCreateViewModel model)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync();

        if (permissions.Any(x =>
            string.Equals(x.Code.Trim(), model.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;

        var permission = _mapper.Map<Permission>(model);

        Normalize(permission);

        await _unitOfWork.Permissions.AddAsync(permission);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(PermissionEditViewModel model)
    {
        var permission = await _unitOfWork.Permissions.GetByIdAsync(model.Id);

        if (permission == null)
            return false;

        var permissions = await _unitOfWork.Permissions.GetAllAsync();

        if (permissions.Any(x =>
            x.Id != model.Id &&
            string.Equals(x.Code.Trim(), model.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;

        permission.Module = model.Module;
        permission.Code = model.Code;
        permission.Name = model.Name;
        permission.Description = model.Description;
        permission.IsActive = model.IsActive;
        permission.DisplayOrder = model.DisplayOrder;

        Normalize(permission);

        _unitOfWork.Permissions.Update(permission);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var permission = await _unitOfWork.Permissions.GetByIdAsync(id);

        if (permission == null)
            return false;

        _unitOfWork.Permissions.Delete(permission);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<int> SeedDefaultPermissionsAsync()
    {
        var existing = await _unitOfWork.Permissions.GetAllAsync();

        var defaults = GetDefaultPermissions();

        var added = 0;

        foreach (var item in defaults)
        {
            var exists = existing.Any(x =>
                string.Equals(x.Code, item.Code, StringComparison.OrdinalIgnoreCase));

            if (exists)
                continue;

            await _unitOfWork.Permissions.AddAsync(item);
            added++;
        }

        if (added > 0)
            await _unitOfWork.SaveChangesAsync();

        return added;
    }

    private static void Normalize(Permission permission)
    {
        permission.Module = permission.Module.Trim();
        permission.Code = permission.Code.Trim();
        permission.Name = permission.Name.Trim();
        permission.Description = string.IsNullOrWhiteSpace(permission.Description)
            ? null
            : permission.Description.Trim();
    }

    private static List<Permission> GetDefaultPermissions()
    {
        var order = 0;

        Permission P(string module, string code, string name, string? description = null)
        {
            order += 10;

            return new Permission
            {
                Module = module,
                Code = code,
                Name = name,
                Description = description,
                IsActive = true,
                DisplayOrder = order
            };
        }

        return new List<Permission>
        {
            P("Dashboard", "Dashboard.View", "View Dashboard"),

            P("Companies", "Companies.View", "View Companies"),
            P("Companies", "Companies.Create", "Create Company"),
            P("Companies", "Companies.Edit", "Edit Company"),
            P("Companies", "Companies.Delete", "Delete Company"),

            P("Branches", "Branches.View", "View Branches"),
            P("Branches", "Branches.Create", "Create Branch"),
            P("Branches", "Branches.Edit", "Edit Branch"),
            P("Branches", "Branches.Delete", "Delete Branch"),

            P("Departments", "Departments.View", "View Departments"),
            P("Departments", "Departments.Create", "Create Department"),
            P("Departments", "Departments.Edit", "Edit Department"),
            P("Departments", "Departments.Delete", "Delete Department"),

            P("Employees", "Employees.View", "View Employees"),
            P("Employees", "Employees.Create", "Create Employee"),
            P("Employees", "Employees.Edit", "Edit Employee"),
            P("Employees", "Employees.Delete", "Delete Employee"),

            P("Devices", "Devices.View", "View Devices"),
            P("Devices", "Devices.Create", "Create Device"),
            P("Devices", "Devices.Edit", "Edit Device"),
            P("Devices", "Devices.Delete", "Delete Device"),

            P("Shifts", "Shifts.View", "View Shifts"),
            P("Shifts", "Shifts.Create", "Create Shift"),
            P("Shifts", "Shifts.Edit", "Edit Shift"),
            P("Shifts", "Shifts.Delete", "Delete Shift"),

            P("Employee Shifts", "EmployeeShifts.View", "View Employee Shifts"),
            P("Employee Shifts", "EmployeeShifts.Create", "Assign Employee Shift"),
            P("Employee Shifts", "EmployeeShifts.Edit", "Edit Employee Shift"),
            P("Employee Shifts", "EmployeeShifts.Delete", "Delete Employee Shift"),

            P("Attendance Records", "AttendanceRecords.View", "View Attendance Records"),
            P("Attendance Records", "AttendanceRecords.Create", "Create Attendance Record"),
            P("Attendance Records", "AttendanceRecords.Edit", "Edit Attendance Record"),
            P("Attendance Records", "AttendanceRecords.Delete", "Delete Attendance Record"),

            P("Attendance Processing", "AttendanceProcessing.View", "View Attendance Processing"),

            P("Attendance Reports", "AttendanceReports.View", "View Attendance Reports"),
            P("Attendance Reports", "AttendanceReports.Export", "Export Attendance Reports"),

            P("Holidays", "Holidays.View", "View Holidays"),
            P("Holidays", "Holidays.Create", "Create Holiday"),
            P("Holidays", "Holidays.Edit", "Edit Holiday"),
            P("Holidays", "Holidays.Delete", "Delete Holiday"),

            P("Leave Requests", "LeaveRequests.View", "View Leave Requests"),
            P("Leave Requests", "LeaveRequests.Create", "Create Leave"),
            P("Leave Requests", "LeaveRequests.Edit", "Edit Leave"),
            P("Leave Requests", "LeaveRequests.Delete", "Delete Leave"),
            P("Leave Requests", "LeaveRequests.Approve", "Approve Leave"),

            P("System Users", "SystemUsers.View", "View System Users"),
            P("System Users", "SystemUsers.Create", "Create System User"),
            P("System Users", "SystemUsers.Edit", "Edit System User"),
            P("System Users", "SystemUsers.Delete", "Delete System User"),

            P("Permissions", "Permissions.View", "View Permissions"),
            P("Permissions", "Permissions.Create", "Create Permission"),
            P("Permissions", "Permissions.Edit", "Edit Permission"),
            P("Permissions", "Permissions.Delete", "Delete Permission"),
            P("Permissions", "Permissions.Assign", "Assign User Permissions"),

            P("Announcements", "Announcements.Create", "Create Announcements"),
            P("Announcements", "Announcements.Publish", "Publish Announcements"),
            P("Announcements", "Announcements.Archive", "Archive Announcements"),
            P("Announcements", "Announcements.Delete", "Delete Announcements"),
            P("Announcements", "Announcements.ManageTemplates", "Manage Announcement Templates"),
            P("Announcements", "Announcements.ManageSignatures", "Manage Announcement Signatures"),
            P("Announcements", "Announcements.ModerateComments", "Moderate Announcement Comments"),
            P("Announcements", "Announcements.ViewReadReports", "View Announcement Read Reports")
        };
    }
}
