using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.Shifts.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class ShiftService : IShiftService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public ShiftService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<ShiftListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        var result = _mapper.Map<IEnumerable<ShiftListViewModel>>(shifts);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.StartTime).ToList();
    }

    public async Task<ShiftDetailsViewModel?> GetByIdAsync(int id)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(id);

        if (shift == null)
            return null;

        return _mapper.Map<ShiftDetailsViewModel>(shift);
    }

    public async Task<ShiftEditViewModel?> GetEditByIdAsync(int id)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(id);

        if (shift == null)
            return null;

        return _mapper.Map<ShiftEditViewModel>(shift);
    }

    public async Task<bool> CreateAsync(ShiftCreateViewModel model)
    {
        if (await CodeExistsAsync(model.Code))
            return false;

        var shift = _mapper.Map<Shift>(model);

        await _unitOfWork.Shifts.AddAsync(shift);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(ShiftEditViewModel model)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(model.Id);

        if (shift == null)
            return false;

        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        var duplicateCode = shifts.Any(x =>
            x.Id != model.Id &&
            x.Code.Equals(model.Code, StringComparison.OrdinalIgnoreCase));

        if (duplicateCode)
            return false;

        shift.Code = model.Code;
        shift.Name = model.Name;
        shift.StartTime = model.StartTime;
        shift.EndTime = model.EndTime;
        shift.WorkingHours = model.WorkingHours;
        shift.GraceInMinutes = model.GraceInMinutes;
        shift.GraceOutMinutes = model.GraceOutMinutes;
        shift.IsNightShift = model.IsNightShift;
        shift.IsActive = model.IsActive;

        _unitOfWork.Shifts.Update(shift);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(id);

        if (shift == null)
            return false;

        _unitOfWork.Shifts.Delete(shift);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        return shifts.Any(x =>
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}
