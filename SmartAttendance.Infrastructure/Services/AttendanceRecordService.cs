using AutoMapper;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceRecordService : IAttendanceRecordService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public AttendanceRecordService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<AttendanceRecordListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var records = await _unitOfWork.AttendanceRecords.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var devices = await _unitOfWork.Devices.GetAllAsync();

        var employeeLookup = employees.ToDictionary(x => x.Id, x => new
        {
            x.EmployeeNo,
            x.FullName
        });

        var deviceLookup = devices.ToDictionary(x => x.Id, x => x.Name);

        var result = records.Select(record =>
        {
            var model = _mapper.Map<AttendanceRecordListViewModel>(record);

            if (employeeLookup.TryGetValue(record.EmployeeId, out var employee))
            {
                model.EmployeeNo = employee.EmployeeNo;
                model.EmployeeName = employee.FullName;
            }

            if (record.DeviceId.HasValue && deviceLookup.TryGetValue(record.DeviceId.Value, out var deviceName))
                model.DeviceName = deviceName;

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.DeviceName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Notes != null && x.Notes.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.Status.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Source.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result
            .OrderByDescending(x => x.AttendanceDate)
            .ThenBy(x => x.EmployeeName)
            .ToList();
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
