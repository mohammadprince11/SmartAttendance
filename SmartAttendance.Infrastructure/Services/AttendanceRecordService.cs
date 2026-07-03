using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceRecordService : IAttendanceRecordService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ApplicationDbContext _dbContext;

    public AttendanceRecordService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dbContext = dbContext;
    }

    public async Task<int> CountAsync(string? searchTerm = null)
    {
        return await ApplySearch(_dbContext.AttendanceRecords.AsNoTracking(), searchTerm)
            .CountAsync();
    }

    public async Task<IEnumerable<AttendanceRecordListViewModel>> GetAllAsync(string? searchTerm = null, int maxResults = 50)
    {
        maxResults = maxResults <= 0 ? 50 : Math.Min(maxResults, 100);

        var query = ApplySearch(
            _dbContext.AttendanceRecords
                .AsNoTracking()
                .Include(x => x.Employee)
                .Include(x => x.Device),
            searchTerm);

        return await query
            .OrderByDescending(x => x.AttendanceDate)
            .ThenByDescending(x => x.CheckIn)
            .Take(maxResults)
            .Select(x => new AttendanceRecordListViewModel
            {
                Id = x.Id,
                EmployeeId = x.EmployeeId,
                EmployeeNo = x.Employee.EmployeeNo,
                EmployeeName = x.Employee.FullName,
                AttendanceDate = x.AttendanceDate,
                CheckIn = x.CheckIn,
                CheckOut = x.CheckOut,
                Source = x.Source,
                Status = x.Status,
                DeviceId = x.DeviceId,
                DeviceName = x.Device != null ? x.Device.Name : string.Empty,
                Notes = x.Notes
            })
            .ToListAsync();
    }

    private IQueryable<AttendanceRecord> ApplySearch(IQueryable<AttendanceRecord> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return query;

        var term = searchTerm.Trim();

        var hasStatus = Enum.TryParse<AttendanceStatus>(term, true, out var parsedStatus);
        var hasSource = Enum.TryParse<AttendanceSource>(term, true, out var parsedSource);

        return query.Where(x =>
            x.Employee.EmployeeNo.Contains(term) ||
            x.Employee.FullName.Contains(term) ||
            (x.Device != null && x.Device.Name.Contains(term)) ||
            (x.Notes != null && x.Notes.Contains(term)) ||
            (hasStatus && x.Status == parsedStatus) ||
            (hasSource && x.Source == parsedSource));
    }

    public async Task<AttendanceRecordDetailsViewModel?> GetByIdAsync(int id)
    {
        var record = await _unitOfWork.AttendanceRecords.GetByIdAsync(id);

        if (record == null)
            return null;

        var employee = await _unitOfWork.Employees.GetByIdAsync(record.EmployeeId);
        Device? device = null;

        if (record.DeviceId.HasValue)
            device = await _unitOfWork.Devices.GetByIdAsync(record.DeviceId.Value);

        var model = _mapper.Map<AttendanceRecordDetailsViewModel>(record);

        model.EmployeeNo = employee?.EmployeeNo ?? string.Empty;
        model.EmployeeName = employee?.FullName ?? string.Empty;
        model.DeviceName = device?.Name ?? string.Empty;

        return model;
    }

    public async Task<AttendanceRecordEditViewModel?> GetEditByIdAsync(int id)
    {
        var record = await _unitOfWork.AttendanceRecords.GetByIdAsync(id);

        if (record == null)
            return null;

        return _mapper.Map<AttendanceRecordEditViewModel>(record);
    }

    public async Task<bool> CreateAsync(AttendanceRecordCreateViewModel model)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        if (model.DeviceId.HasValue)
        {
            var device = await _unitOfWork.Devices.GetByIdAsync(model.DeviceId.Value);

            if (device == null)
                return false;
        }

        if (model.CheckOut.HasValue && model.CheckOut.Value < model.CheckIn)
            return false;

        var record = _mapper.Map<AttendanceRecord>(model);

        await _unitOfWork.AttendanceRecords.AddAsync(record);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(AttendanceRecordEditViewModel model)
    {
        var record = await _unitOfWork.AttendanceRecords.GetByIdAsync(model.Id);

        if (record == null)
            return false;

        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        if (model.DeviceId.HasValue)
        {
            var device = await _unitOfWork.Devices.GetByIdAsync(model.DeviceId.Value);

            if (device == null)
                return false;
        }

        if (model.CheckOut.HasValue && model.CheckOut.Value < model.CheckIn)
            return false;

        record.EmployeeId = model.EmployeeId;
        record.AttendanceDate = model.AttendanceDate;
        record.CheckIn = model.CheckIn;
        record.CheckOut = model.CheckOut;
        record.Source = model.Source;
        record.Status = model.Status;
        record.DeviceId = model.DeviceId;
        record.Notes = model.Notes;

        _unitOfWork.AttendanceRecords.Update(record);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var record = await _unitOfWork.AttendanceRecords.GetByIdAsync(id);

        if (record == null)
            return false;

        _unitOfWork.AttendanceRecords.Delete(record);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync()
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();

        return employees
            .Where(x => x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new EmployeeListViewModel
            {
                Id = x.Id,
                EmployeeNo = x.EmployeeNo,
                FullName = x.FullName,
                NationalId = x.NationalId,
                Phone = x.Phone,
                Email = x.Email,
                HireDate = x.HireDate,
                BirthDate = x.BirthDate,
                IsActive = x.IsActive,
                DepartmentId = x.DepartmentId
            })
            .ToList();
    }

    public async Task<IEnumerable<DeviceListViewModel>> GetDevicesForDropdownAsync()
    {
        var devices = await _unitOfWork.Devices.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();

        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        return devices
            .Where(x => x.IsActive && x.IsEnabled)
            .OrderBy(x => x.Name)
            .Select(x => new DeviceListViewModel
            {
                Id = x.Id,
                Name = x.Name,
                IpAddress = x.IpAddress,
                Port = x.Port,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                FirmwareVersion = x.FirmwareVersion,
                IsActive = x.IsActive,
                IsEnabled = x.IsEnabled,
                BranchId = x.BranchId,
                BranchName = branchLookup.TryGetValue(x.BranchId, out var branchName) ? branchName : string.Empty
            })
            .ToList();
    }
}
