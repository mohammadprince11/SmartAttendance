using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Application.Shifts.Services;

public interface IShiftService
{
    Task<IEnumerable<ShiftListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<ShiftDetailsViewModel?> GetByIdAsync(int id);

    Task<ShiftEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(ShiftCreateViewModel model);

    Task<bool> UpdateAsync(ShiftEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> CodeExistsAsync(string code);
}
