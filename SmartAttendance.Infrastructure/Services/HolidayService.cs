using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.Holidays.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class HolidayService : IHolidayService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public HolidayService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<HolidayListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var holidays = await _unitOfWork.Holidays.GetAllAsync();

        var result = _mapper.Map<IEnumerable<HolidayListViewModel>>(holidays);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Description != null && x.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderBy(x => x.HolidayDate)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<HolidayDetailsViewModel?> GetByIdAsync(int id)
    {
        var holiday = await _unitOfWork.Holidays.GetByIdAsync(id);

        if (holiday == null)
            return null;

        return _mapper.Map<HolidayDetailsViewModel>(holiday);
    }

    public async Task<HolidayEditViewModel?> GetEditByIdAsync(int id)
    {
        var holiday = await _unitOfWork.Holidays.GetByIdAsync(id);

        if (holiday == null)
            return null;

        return _mapper.Map<HolidayEditViewModel>(holiday);
    }

    public async Task<bool> CreateAsync(HolidayCreateViewModel model)
    {
        var holiday = _mapper.Map<Holiday>(model);

        await _unitOfWork.Holidays.AddAsync(holiday);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(HolidayEditViewModel model)
    {
        var holiday = await _unitOfWork.Holidays.GetByIdAsync(model.Id);

        if (holiday == null)
            return false;

        holiday.Name = model.Name;
        holiday.HolidayDate = model.HolidayDate;
        holiday.IsRecurring = model.IsRecurring;
        holiday.Description = model.Description;

        _unitOfWork.Holidays.Update(holiday);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var holiday = await _unitOfWork.Holidays.GetByIdAsync(id);

        if (holiday == null)
            return false;

        _unitOfWork.Holidays.Delete(holiday);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }
}
