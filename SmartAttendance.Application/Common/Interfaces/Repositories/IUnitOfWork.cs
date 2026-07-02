using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Common.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    ICompanyRepository Companies { get; }

    IGenericRepository<Branch> Branches { get; }

    IGenericRepository<Department> Departments { get; }

    IGenericRepository<Employee> Employees { get; }

    IGenericRepository<Device> Devices { get; }

    IGenericRepository<Shift> Shifts { get; }

    IGenericRepository<EmployeeShift> EmployeeShifts { get; }

    IGenericRepository<AttendanceRecord> AttendanceRecords { get; }

    IGenericRepository<Holiday> Holidays { get; }

    IGenericRepository<LeaveRequest> LeaveRequests { get; }

    IGenericRepository<SystemUser> SystemUsers { get; }

    IGenericRepository<Permission> Permissions { get; }

    IGenericRepository<SystemUserPermission> SystemUserPermissions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
