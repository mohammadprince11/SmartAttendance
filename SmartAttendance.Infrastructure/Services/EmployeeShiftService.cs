using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.Shifts.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class EmployeeShiftService : IEmployeeShiftService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public EmployeeShiftService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<EmployeeShiftListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var employeeShifts = await _unitOfWork.EmployeeShifts.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        var employeeLookup = employees.ToDictionary(x => x.Id, x => new
        {
            x.EmployeeNo,
            x.FullName
        });

        var shiftLookup = shifts.ToDictionary(x => x.Id, x => new
        {
            x.Code,
            x.Name
        });

        var result = employeeShifts.Select(employeeShift =>
        {
            var model = _mapper.Map<EmployeeShiftListViewModel>(employeeShift);

            if (employeeLookup.TryGetValue(employeeShift.EmployeeId, out var employee))
            {
                model.EmployeeNo = employee.EmployeeNo;
                model.EmployeeName = employee.FullName;
            }

            if (shiftLookup.TryGetValue(employeeShift.ShiftId, out var shift))
            {
                model.ShiftCode = shift.Code;
                model.ShiftName = shift.Name;
            }

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result
            .OrderByDescending(x => x.IsCurrent)
            .ThenBy(x => x.EmployeeName)
            .ThenByDescending(x => x.EffectiveFrom)
            .ToList();
    }

    public async Task<EmployeeShiftDetailsViewModel?> GetByIdAsync(int id)
    {
        var employeeShift = await _unitOfWork.EmployeeShifts.GetByIdAsync(id);

        if (employeeShift == null)
            return null;

        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeShift.EmployeeId);
        var shift = await _unitOfWork.Shifts.GetByIdAsync(employeeShift.ShiftId);

        var model = _mapper.Map<EmployeeShiftDetailsViewModel>(employeeShift);

        model.EmployeeNo = employee?.EmployeeNo ?? string.Empty;
        model.EmployeeName = employee?.FullName ?? string.Empty;
        model.ShiftCode = shift?.Code ?? string.Empty;
        model.ShiftName = shift?.Name ?? string.Empty;

        return model;
    }

    public async Task<EmployeeShiftEditViewModel?> GetEditByIdAsync(int id)
    {
        var employeeShift = await _unitOfWork.EmployeeShifts.GetByIdAsync(id);

        if (employeeShift == null)
            return null;

        return _mapper.Map<EmployeeShiftEditViewModel>(employeeShift);
    }

    public async Task<bool> CreateAsync(EmployeeShiftCreateViewModel model)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);
        var shift = await _unitOfWork.Shifts.GetByIdAsync(model.ShiftId);

        if (employee == null || shift == null)
            return false;

        if (model.EffectiveTo.HasValue && model.EffectiveTo.Value < model.EffectiveFrom)
            return false;

        var employeeShifts = await _unitOfWork.EmployeeShifts.GetAllAsync();

        if (model.IsCurrent)
        {
            foreach (var oldShift in employeeShifts.Where(x => x.EmployeeId == model.EmployeeId && x.IsCurrent))
            {
                oldShift.IsCurrent = false;

                if (!oldShift.EffectiveTo.HasValue || oldShift.EffectiveTo.Value >= model.EffectiveFrom)
                    oldShift.EffectiveTo = model.EffectiveFrom.AddDays(-1);

                _unitOfWork.EmployeeShifts.Update(oldShift);
            }
        }

        var employeeShift = _mapper.Map<EmployeeShift>(model);

        await _unitOfWork.EmployeeShifts.AddAsync(employeeShift);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(EmployeeShiftEditViewModel model)
    {
        var employeeShift = await _unitOfWork.EmployeeShifts.GetByIdAsync(model.Id);

        if (employeeShift == null)
            return false;

        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);
        var shift = await _unitOfWork.Shifts.GetByIdAsync(model.ShiftId);

        if (employee == null || shift == null)
            return false;

        if (model.EffectiveTo.HasValue && model.EffectiveTo.Value < model.EffectiveFrom)
            return false;

        employeeShift.EmployeeId = model.EmployeeId;
        employeeShift.ShiftId = model.ShiftId;
        employeeShift.EffectiveFrom = model.EffectiveFrom;
        employeeShift.EffectiveTo = model.EffectiveTo;
        employeeShift.IsCurrent = model.IsCurrent;

        _unitOfWork.EmployeeShifts.Update(employeeShift);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var employeeShift = await _unitOfWork.EmployeeShifts.GetByIdAsync(id);

        if (employeeShift == null)
            return false;

        _unitOfWork.EmployeeShifts.Delete(employeeShift);
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

    public async Task<IEnumerable<ShiftListViewModel>> GetShiftsForDropdownAsync()
    {
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        return shifts
            .Where(x => x.IsActive)
            .OrderBy(x => x.StartTime)
            .Select(x => new ShiftListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                WorkingHours = x.WorkingHours,
                GraceInMinutes = x.GraceInMinutes,
                GraceOutMinutes = x.GraceOutMinutes,
                IsNightShift = x.IsNightShift,
                IsActive = x.IsActive
            })
            .ToList();
    }
}
