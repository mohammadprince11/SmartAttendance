using SmartAttendance.Application.Holidays.ViewModels;

namespace SmartAttendance.Application.Holidays.Services;

public interface IHolidayService
{
    Task<IEnumerable<HolidayListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<HolidayDetailsViewModel?> GetByIdAsync(int id);

    Task<HolidayEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(HolidayCreateViewModel model);

    Task<bool> UpdateAsync(HolidayEditViewModel model);

    Task<bool> DeleteAsync(int id);
}
