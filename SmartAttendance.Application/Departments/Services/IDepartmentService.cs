using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;

namespace SmartAttendance.Application.Departments.Services;

public interface IDepartmentService
{
    Task<IEnumerable<DepartmentListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<DepartmentDetailsViewModel?> GetByIdAsync(int id);

    Task<DepartmentEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(DepartmentCreateViewModel model);

    Task<bool> UpdateAsync(DepartmentEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> CodeExistsAsync(string code);

    Task<IEnumerable<CompanyListViewModel>> GetCompaniesForDropdownAsync();

    Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync();
}
