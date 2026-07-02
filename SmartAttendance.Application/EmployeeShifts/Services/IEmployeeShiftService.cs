using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Application.EmployeeShifts.Services;

public interface IEmployeeShiftService
{
    Task<IEnumerable<EmployeeShiftListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<EmployeeShiftDetailsViewModel?> GetByIdAsync(int id);

    Task<EmployeeShiftEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(EmployeeShiftCreateViewModel model);

    Task<bool> UpdateAsync(EmployeeShiftEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync();

    Task<IEnumerable<ShiftListViewModel>> GetShiftsForDropdownAsync();
}
