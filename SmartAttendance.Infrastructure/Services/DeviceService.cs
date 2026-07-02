using AutoMapper;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class DeviceService : IDeviceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public DeviceService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<DeviceListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var devices = await _unitOfWork.Devices.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();

        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        var result = devices.Select(device =>
        {
            var model = _mapper.Map<DeviceListViewModel>(device);

            model.BranchName = branchLookup.TryGetValue(device.BranchId, out var branchName)
                ? branchName
                : string.Empty;

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.IpAddress.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.SerialNumber.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Model != null && x.Model.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.BranchName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.Name).ToList();
    }

    public async Task<DeviceDetailsViewModel?> GetByIdAsync(int id)
    {
        var device = await _unitOfWork.Devices.GetByIdAsync(id);

        if (device == null)
            return null;

        var branch = await _unitOfWork.Branches.GetByIdAsync(device.BranchId);

        var model = _mapper.Map<DeviceDetailsViewModel>(device);
        model.BranchName = branch?.Name ?? string.Empty;

        return model;
    }

    public async Task<DeviceEditViewModel?> GetEditByIdAsync(int id)
    {
        var device = await _unitOfWork.Devices.GetByIdAsync(id);

        if (device == null)
            return null;

        return _mapper.Map<DeviceEditViewModel>(device);
    }

    public async Task<bool> CreateAsync(DeviceCreateViewModel model)
    {
        if (await SerialNumberExistsAsync(model.SerialNumber))
            return false;

        var branch = await _unitOfWork.Branches.GetByIdAsync(model.BranchId);

        if (branch == null)
            return false;

        var device = _mapper.Map<Device>(model);

        await _unitOfWork.Devices.AddAsync(device);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(DeviceEditViewModel model)
    {
        var device = await _unitOfWork.Devices.GetByIdAsync(model.Id);

        if (device == null)
            return false;

        var branch = await _unitOfWork.Branches.GetByIdAsync(model.BranchId);

        if (branch == null)
            return false;

        var devices = await _unitOfWork.Devices.GetAllAsync();

        var duplicateSerial = devices.Any(x =>
            x.Id != model.Id &&
            x.SerialNumber.Equals(model.SerialNumber, StringComparison.OrdinalIgnoreCase));

        if (duplicateSerial)
            return false;

        device.Name = model.Name;
        device.IpAddress = model.IpAddress;
        device.Port = model.Port;
        device.SerialNumber = model.SerialNumber;
        device.Model = model.Model;
        device.FirmwareVersion = model.FirmwareVersion;
        device.IsActive = model.IsActive;
        device.IsEnabled = model.IsEnabled;
        device.BranchId = model.BranchId;

        _unitOfWork.Devices.Update(device);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var device = await _unitOfWork.Devices.GetByIdAsync(id);

        if (device == null)
            return false;

        _unitOfWork.Devices.Delete(device);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> SerialNumberExistsAsync(string serialNumber)
    {
        var devices = await _unitOfWork.Devices.GetAllAsync();

        return devices.Any(x =>
            x.SerialNumber.Equals(serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync()
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();

        return branches
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new BranchListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive,
                CompanyId = x.CompanyId
            })
            .ToList();
    }
}
