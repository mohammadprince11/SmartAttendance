using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories.Common;

namespace SmartAttendance.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public ICompanyRepository Companies { get; }

    public IGenericRepository<Branch> Branches { get; }

    public IGenericRepository<Department> Departments { get; }

    public IGenericRepository<Employee> Employees { get; }

    public IGenericRepository<Device> Devices { get; }

    public IGenericRepository<Shift> Shifts { get; }

    public IGenericRepository<EmployeeShift> EmployeeShifts { get; }

    public IGenericRepository<AttendanceRecord> AttendanceRecords { get; }

    public IGenericRepository<Holiday> Holidays { get; }

    public IGenericRepository<LeaveRequest> LeaveRequests { get; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;

        Companies = new CompanyRepository(context);
        Branches = new GenericRepository<Branch>(context);
        Departments = new GenericRepository<Department>(context);
        Employees = new GenericRepository<Employee>(context);
        Devices = new GenericRepository<Device>(context);
        Shifts = new GenericRepository<Shift>(context);
        EmployeeShifts = new GenericRepository<EmployeeShift>(context);
        AttendanceRecords = new GenericRepository<AttendanceRecord>(context);
        Holidays = new GenericRepository<Holiday>(context);
        LeaveRequests = new GenericRepository<LeaveRequest>(context);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
