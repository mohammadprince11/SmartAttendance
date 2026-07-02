using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories.Common;

namespace SmartAttendance.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;

        Companies = new CompanyRepository(_context);
        Branches = new GenericRepository<Branch>(_context);
        Departments = new GenericRepository<Department>(_context);
        Employees = new GenericRepository<Employee>(_context);
        Devices = new GenericRepository<Device>(_context);
        Shifts = new GenericRepository<Shift>(_context);
        EmployeeShifts = new GenericRepository<EmployeeShift>(_context);
        AttendanceRecords = new GenericRepository<AttendanceRecord>(_context);
        Holidays = new GenericRepository<Holiday>(_context);
    }

    public ICompanyRepository Companies { get; }

    public IGenericRepository<Branch> Branches { get; }

    public IGenericRepository<Department> Departments { get; }

    public IGenericRepository<Employee> Employees { get; }

    public IGenericRepository<Device> Devices { get; }

    public IGenericRepository<Shift> Shifts { get; }

    public IGenericRepository<EmployeeShift> EmployeeShifts { get; }

    public IGenericRepository<AttendanceRecord> AttendanceRecords { get; }

    public IGenericRepository<Holiday> Holidays { get; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}